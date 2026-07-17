using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace Perfoptimize.App;

/// <summary>
/// Thin wrapper over Velopack's update flow, kept prudent to match the rest of
/// the app: it only ever <em>checks</em> and reports on its own — a new version
/// is never downloaded or applied without the user pressing "Mettre à jour".
/// When the app is running from source (not installed via the Velopack
/// installer) every call is a safe no-op.
/// </summary>
public sealed class UpdateService
{
    // Releases + the update feed are published to GitHub Releases by
    // .github/workflows/release.yml.
    private const string RepoUrl = "https://github.com/MathieuBal/Perfoptimize";

    private readonly UpdateManager _manager = new(new GithubSource(RepoUrl, accessToken: null, prerelease: false));

    public sealed record UpdateStatus(bool Installed, bool Available, string CurrentVersion, string? NewVersion, string? Error = null);

    /// <summary>Looks for a newer release without downloading anything.</summary>
    public async Task<UpdateStatus> CheckAsync()
    {
        if (!_manager.IsInstalled)
            return new UpdateStatus(Installed: false, Available: false, CurrentVersion(), null);
        try
        {
            var info = await _manager.CheckForUpdatesAsync();
            return info is null
                ? new UpdateStatus(true, false, CurrentVersion(), null)
                : new UpdateStatus(true, true, CurrentVersion(), info.TargetFullRelease.Version.ToString());
        }
        catch (Exception ex)
        {
            // Offline / rate-limited / no releases yet — report, don't crash.
            return new UpdateStatus(true, false, CurrentVersion(), null, ex.Message);
        }
    }

    /// <summary>
    /// Downloads the newest release and restarts into it. Only ever called from
    /// an explicit user action. Does not return on success (the process exits).
    /// </summary>
    public async Task<(bool Ok, string Message)> ApplyAsync()
    {
        if (!_manager.IsInstalled)
            return (false, "L'auto-update n'est disponible que sur une version installée (pas en développement).");
        try
        {
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null)
                return (false, "Aucune mise à jour disponible.");

            await _manager.DownloadUpdatesAsync(info);
            _manager.ApplyUpdatesAndRestart(info.TargetFullRelease); // exits and relaunches
            return (true, "Redémarrage sur la nouvelle version…");   // unreached in practice
        }
        catch (Exception ex)
        {
            return (false, $"Échec de la mise à jour : {ex.Message}");
        }
    }

    private string CurrentVersion() =>
        _manager.CurrentVersion?.ToString()
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "dev";
}
