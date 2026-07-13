using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Perfoptimize.Core.Library;

public enum GameSource { Steam, Folder }

/// <summary>A game found installed on disk (independent of the built-in profiles).</summary>
public sealed class InstalledGame
{
    public string Name { get; init; } = "";
    public string InstallDir { get; init; } = "";
    public GameSource Source { get; init; }
    public string? AppId { get; init; }
}

/// <summary>
/// Discovers games installed on disk by walking the game platforms' own
/// bookkeeping — Steam's library folders and app manifests. This is what lets
/// the assistant work on any installed game, not only the profiled ones.
/// The library-root discovery is I/O; the parsing is delegated to
/// <see cref="SteamVdf"/>, so the scan is testable against a fake library tree.
/// </summary>
public sealed class InstalledGameScanner
{
    /// <summary>Locates Steam installation roots (registry + common paths on Windows).</summary>
    public static IReadOnlyList<string> DefaultSteamRoots()
    {
        var roots = new List<string>();
        if (OperatingSystem.IsWindows())
            AddWindowsSteamRoots(roots);

        // Common cross-platform fallbacks (Proton/Linux, default installs).
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var candidate in new[]
                 {
                     Path.Combine(home, ".steam", "steam"),
                     Path.Combine(home, ".local", "share", "Steam"),
                     @"C:\Program Files (x86)\Steam",
                 })
        {
            if (Directory.Exists(candidate) && !roots.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                roots.Add(candidate);
        }
        return roots;
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindowsSteamRoots(List<string> roots)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
            if (key?.GetValue("SteamPath") is string path && Directory.Exists(path))
                roots.Add(path.Replace('/', '\\'));
        }
        catch
        {
            // Registry access can be restricted; the common-path fallback covers it.
        }
    }

    /// <summary>Scans the given Steam roots for installed games. Never throws.</summary>
    public IReadOnlyList<InstalledGame> ScanSteam(IEnumerable<string> steamRoots)
    {
        var games = new Dictionary<string, InstalledGame>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in steamRoots)
        {
            // Every Steam root is itself a library; libraryfolders.vdf adds more.
            var libraries = new List<string> { root };
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                try
                {
                    libraries.AddRange(SteamVdf.ParseLibraryFolders(File.ReadAllText(vdf)));
                }
                catch (IOException) { }
            }

            foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var steamapps = Path.Combine(library, "steamapps");
                if (!Directory.Exists(steamapps))
                    continue;
                foreach (var manifest in SafeEnumerate(steamapps, "appmanifest_*.acf"))
                {
                    SteamApp? app;
                    try { app = SteamVdf.ParseAppManifest(File.ReadAllText(manifest)); }
                    catch (IOException) { continue; }
                    if (app is not { } a)
                        continue;

                    var installDir = Path.Combine(steamapps, "common", a.InstallDir);
                    if (!Directory.Exists(installDir))
                        continue;
                    games[installDir] = new InstalledGame
                    {
                        Name = a.Name, InstallDir = installDir, Source = GameSource.Steam, AppId = a.AppId,
                    };
                }
            }
        }

        return games.Values.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Scans the machine's default Steam locations.</summary>
    public IReadOnlyList<InstalledGame> Scan() => ScanSteam(DefaultSteamRoots());

    private static IEnumerable<string> SafeEnumerate(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
