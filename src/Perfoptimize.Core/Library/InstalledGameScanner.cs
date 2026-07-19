using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Perfoptimize.Core.Library;

public enum GameSource { Steam, Epic, Folder }

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
    /// <summary>Locates Steam roots: registry, common paths, and a probe of every drive.</summary>
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
            if (Directory.Exists(candidate))
                roots.Add(candidate);
        }

        // Probe EVERY fixed drive: users routinely put a SteamLibrary on D:/K:/…,
        // and libraryfolders.vdf only helps once the main install is found.
        var driveRoots = SafeFixedDriveRoots();
        roots.AddRange(SteamLibraryRootsUnderDrives(driveRoots));

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// From a set of drive roots (C:\, D:\, …), returns the Steam library
    /// directories that actually exist under the usual locations. Pure over the
    /// filesystem, so it is testable with fake "drives".
    /// </summary>
    public static IReadOnlyList<string> SteamLibraryRootsUnderDrives(IEnumerable<string> driveRoots)
    {
        string[] relatives =
        [
            "SteamLibrary", "Steam", "SteamGames",
            Path.Combine("Games", "SteamLibrary"),
            Path.Combine("Program Files (x86)", "Steam"),
            Path.Combine("Program Files", "Steam"),
        ];
        var found = new List<string>();
        foreach (var drive in driveRoots)
        {
            foreach (var relative in relatives)
            {
                var candidate = Path.Combine(drive, relative);
                if (Directory.Exists(Path.Combine(candidate, "steamapps")))
                    found.Add(candidate);
            }
        }
        return found;
    }

    private static IEnumerable<string> SafeFixedDriveRoots()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d is { IsReady: true, DriveType: DriveType.Fixed })
                .Select(d => d.RootDirectory.FullName)
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
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

    /// <summary>Scans Epic Games manifests for installed games. Never throws.</summary>
    public IReadOnlyList<InstalledGame> ScanEpic(string manifestsDir)
    {
        if (!Directory.Exists(manifestsDir))
            return [];
        var games = new List<InstalledGame>();
        foreach (var item in SafeEnumerate(manifestsDir, "*.item"))
        {
            EpicManifest? manifest;
            try { manifest = EpicManifest.Parse(File.ReadAllText(item)); }
            catch (IOException) { continue; }
            if (manifest is { } m && Directory.Exists(m.InstallLocation))
                games.Add(new InstalledGame
                {
                    Name = m.DisplayName, InstallDir = m.InstallLocation, Source = GameSource.Epic,
                });
        }
        return games;
    }

    /// <summary>Default Epic manifests directory (Windows only).</summary>
    public static string DefaultEpicManifestsDir()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
    }

    /// <summary>
    /// Games that ship their OWN launcher (not Steam/Epic) and so leave no
    /// manifest to read — we find them by probing their known install folders on
    /// every drive. Path of Exile 2 is the motivating case: standalone, and
    /// routinely installed off C:.
    /// </summary>
    private static readonly (string Name, string Folder)[] KnownStandaloneGames =
    [
        ("Path of Exile 2", "Path of Exile 2"),
        ("Path of Exile", "Path of Exile"),
    ];

    /// <summary>
    /// Probes each drive for the known standalone game folders under the usual
    /// parents (root, Program Files, GGG's folder, a Games folder). Bounded — it
    /// checks a fixed set of candidate paths, it does not walk the disk.
    /// </summary>
    public IReadOnlyList<InstalledGame> ScanStandaloneFolders(IEnumerable<string> driveRoots)
    {
        var games = new Dictionary<string, InstalledGame>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in driveRoots)
            foreach (var (name, folder) in KnownStandaloneGames)
                foreach (var parent in StandaloneParents(drive))
                {
                    var dir = Path.Combine(parent, folder);
                    if (Directory.Exists(dir))
                        games[dir] = new InstalledGame { Name = name, InstallDir = dir, Source = GameSource.Folder };
                }
        return games.Values.ToList();
    }

    private static IEnumerable<string> StandaloneParents(string drive) =>
    [
        drive,
        Path.Combine(drive, "Program Files (x86)", "Grinding Gear Games"),
        Path.Combine(drive, "Program Files", "Grinding Gear Games"),
        Path.Combine(drive, "Games"),
        Path.Combine(drive, "Program Files (x86)"),
        Path.Combine(drive, "Program Files"),
    ];

    /// <summary>
    /// The concrete locations a full scan looks at (Steam roots, Epic manifests,
    /// standalone parents per drive), so the UI can show WHERE it searched — and
    /// the user can tell whether their disk was even covered.
    /// </summary>
    public static IReadOnlyList<string> InspectedLocations()
    {
        var locations = new List<string>();
        locations.AddRange(DefaultSteamRoots());
        locations.Add(DefaultEpicManifestsDir());
        foreach (var drive in SafeFixedDriveRoots())
            locations.AddRange(StandaloneParents(drive));
        return locations.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Scans every default location across all launchers and drives.</summary>
    public IReadOnlyList<InstalledGame> Scan()
    {
        var all = new Dictionary<string, InstalledGame>(StringComparer.OrdinalIgnoreCase);
        var drives = SafeFixedDriveRoots();
        foreach (var game in ScanSteam(DefaultSteamRoots())
                     .Concat(ScanEpic(DefaultEpicManifestsDir()))
                     .Concat(ScanStandaloneFolders(drives)))
            all[game.InstallDir] = game;
        return all.Values.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

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
