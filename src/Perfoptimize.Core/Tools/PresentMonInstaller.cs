using System.IO.Compression;
using System.Text.Json;
using Perfoptimize.Core.Monitoring.Frametimes;

namespace Perfoptimize.Core.Tools;

/// <summary>One downloadable asset of a GitHub release.</summary>
public readonly record struct ReleaseAsset(string Name, string DownloadUrl);

/// <summary>Outcome of a PresentMon install attempt.</summary>
public sealed class PresentMonInstallResult
{
    public bool Success { get; init; }
    public string? ExecutablePath { get; init; }
    public string? Version { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>
/// Downloads Intel's PresentMon (the no-injection FPS capture tool) from its
/// official GitHub releases so the user can enable real frame-time measurement
/// straight from the UI. Only the pinned GameTechDev/PresentMon repository is
/// ever contacted, and the console build (not the overlay/service) is selected.
/// The asset-selection logic is pure and unit-tested; the HTTP/extraction is
/// best-effort.
/// </summary>
public sealed class PresentMonInstaller
{
    // Pinned official source — never taken from user input.
    private const string LatestReleaseApi =
        "https://api.github.com/repos/GameTechDev/PresentMon/releases/latest";

    private readonly HttpClient _http;

    public PresentMonInstaller(HttpClient http) => _http = http;

    /// <summary>Where the installed PresentMon executable lives.</summary>
    public static string DefaultInstallDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Perfoptimize", "tools");

    /// <summary>
    /// Picks the console executable asset from a release's asset list. PresentMon
    /// ships several builds; we want "PresentMon-*-x64.exe" (the CLI), not the
    /// overlay, the service, or a source archive.
    /// </summary>
    public static ReleaseAsset? SelectConsoleAsset(IEnumerable<ReleaseAsset> assets)
    {
        var candidates = assets
            .Where(a => a.Name.StartsWith("PresentMon", StringComparison.OrdinalIgnoreCase))
            .Where(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                        a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Where(a => !a.Name.Contains("Overlay", StringComparison.OrdinalIgnoreCase) &&
                        !a.Name.Contains("Service", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Prefer a 64-bit standalone .exe, then any x64 asset, then any candidate.
        // FirstOrDefault on a struct list can't signal "none", so match explicitly.
        static ReleaseAsset? First(List<ReleaseAsset> list, Func<ReleaseAsset, bool> match)
        {
            foreach (var a in list)
                if (match(a))
                    return a;
            return null;
        }

        var exe = (bool x64) => First(candidates, a =>
            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            (!x64 || a.Name.Contains("x64", StringComparison.OrdinalIgnoreCase)));

        return exe(true)
               ?? First(candidates, a => a.Name.Contains("x64", StringComparison.OrdinalIgnoreCase))
               ?? exe(false)
               ?? (candidates.Count > 0 ? candidates[0] : (ReleaseAsset?)null);
    }

    /// <summary>Parses the GitHub "latest release" JSON into (tag, assets).</summary>
    public static (string? Tag, IReadOnlyList<ReleaseAsset> Assets) ParseRelease(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in arr.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
                        assets.Add(new ReleaseAsset(name, url));
                }
            }
            return (tag, assets);
        }
        catch (JsonException)
        {
            return (null, []);
        }
    }

    /// <summary>Current install status: the resolved path, or null when absent.</summary>
    public static string? FindInstalled()
    {
        var existing = PresentMonCapture.FindExecutable();
        if (existing is not null)
            return existing;
        var local = Path.Combine(DefaultInstallDirectory(), "PresentMon.exe");
        return File.Exists(local) ? local : null;
    }

    /// <summary>
    /// Downloads and installs the latest PresentMon console build. Returns a
    /// result describing success or the reason it could not complete; never
    /// throws, since this is an optional enhancement.
    /// </summary>
    public async Task<PresentMonInstallResult> InstallLatestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            request.Headers.UserAgent.ParseAdd("Perfoptimize");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Fail($"GitHub returned {(int)response.StatusCode} fetching the PresentMon release.");

            var (tag, assets) = ParseRelease(await response.Content.ReadAsStringAsync(cancellationToken));
            if (SelectConsoleAsset(assets) is not { } asset)
                return Fail("No suitable PresentMon console build was found in the latest release.");

            var directory = DefaultInstallDirectory();
            Directory.CreateDirectory(directory);
            var targetExe = Path.Combine(directory, "PresentMon.exe");

            var bytes = await _http.GetByteArrayAsync(asset.DownloadUrl, cancellationToken);
            if (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ExtractConsoleExeFromZip(bytes, targetExe);
            }
            else
            {
                await File.WriteAllBytesAsync(targetExe, bytes, cancellationToken);
            }

            if (!File.Exists(targetExe))
                return Fail("The download completed but no PresentMon executable was produced.");

            return new PresentMonInstallResult
            {
                Success = true,
                ExecutablePath = targetExe,
                Version = tag,
                Message = $"PresentMon {tag} installed to {targetExe}.",
            };
        }
        catch (Exception ex)
        {
            return Fail($"PresentMon install failed: {ex.Message}");
        }
    }

    private static void ExtractConsoleExeFromZip(byte[] zipBytes, string targetExe)
    {
        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(e =>
                        e.Name.StartsWith("PresentMon", StringComparison.OrdinalIgnoreCase) &&
                        e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        !e.Name.Contains("Overlay", StringComparison.OrdinalIgnoreCase) &&
                        !e.Name.Contains("Service", StringComparison.OrdinalIgnoreCase))
                    ?? archive.Entries.FirstOrDefault(e =>
                        e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        entry?.ExtractToFile(targetExe, overwrite: true);
    }

    private static PresentMonInstallResult Fail(string message) =>
        new() { Success = false, Message = message };
}
