using Perfoptimize.Core.Configs;

namespace Perfoptimize.Core.Library;

/// <summary>One graphics-relevant key read from a config file.</summary>
public readonly record struct ConfigSetting(string Section, string Key, string Value);

/// <summary>A config file found on disk for a game, with its parsed graphics keys.</summary>
public sealed class FoundConfig
{
    public string Path { get; init; } = "";
    public string Format { get; init; } = "Unknown";
    /// <summary>Graphics-relevant keys (INI only for now); empty for non-INI formats.</summary>
    public IReadOnlyList<ConfigSetting> GraphicsSettings { get; init; } = [];
    /// <summary>Total keys in the file, so the UI can say "12 of 340 shown".</summary>
    public int TotalKeys { get; init; }
}

/// <summary>
/// Finds and reads a game's configuration files without needing a hand-written
/// profile. It looks inside the install directory and the usual per-user config
/// locations (Documents\My Games, %LOCALAPPDATA%, %APPDATA%), matches folders to
/// the game by name, and surfaces the graphics-relevant keys of INI files. The
/// matching and graphics-key filtering are pure and unit-tested; the walking is
/// depth- and count-bounded so it never trawls an entire drive.
/// </summary>
public sealed class GameConfigScanner
{
    private const int MaxInstallDirDepth = 3;
    private const int MaxFilesInspected = 400;

    private static readonly string[] ConfigFileNames =
    [
        "config", "settings", "user", "usersettings", "gameusersettings",
        "engine", "scalability", "graphics", "video", "options", "prefs",
    ];
    private static readonly string[] ConfigExtensions = [".ini", ".cfg", ".settings"];

    // Keys whose name suggests a graphics/performance setting worth surfacing.
    private static readonly string[] GraphicsKeywords =
    [
        "quality", "shadow", "texture", "resolution", "vsync", "fullscreen", "window",
        "antialias", "aa", "msaa", "taa", "fxaa", "fps", "framerate", "frame", "gamma",
        "brightness", "effect", "view", "distance", "detail", "render", "dx", "vulkan",
        "bloom", "motionblur", "ambient", "occlusion", "reflection", "volumetric",
        "foliage", "grass", "water", "preset", "lod", "anisotrop", "hairworks", "raytrac",
        "dlss", "fsr", "xess", "upscal", "scaling", "postprocess", "depthoffield", "ssao",
    ];

    /// <summary>Finds config files for a game installed at <paramref name="installDir"/>.</summary>
    public IReadOnlyList<FoundConfig> FindConfigs(string gameName, string installDir)
    {
        var results = new Dictionary<string, FoundConfig>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in EnumerateInstallDirConfigs(installDir))
            TryAdd(results, file);

        foreach (var root in UserConfigRoots())
            foreach (var file in EnumerateMatchingUserConfigs(root, gameName))
                TryAdd(results, file);

        return results.Values
            .OrderByDescending(c => c.GraphicsSettings.Count)
            .ToList();
    }

    /// <summary>Does a config folder's name plausibly belong to this game?</summary>
    public static bool FolderMatchesGame(string folderName, string gameName)
    {
        var f = Normalize(folderName);
        var g = Normalize(gameName);
        if (f.Length < 3 || g.Length < 3)
            return false;
        return f.Contains(g) || g.Contains(f);
    }

    /// <summary>True when a file name looks like a graphics/config file we should read.</summary>
    public static bool LooksLikeConfigFile(string fileName)
    {
        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is ".json" or ".xml")
            return NameStem(fileName) is var stem &&
                   ConfigFileNames.Any(n => stem.Contains(n, StringComparison.OrdinalIgnoreCase));
        if (!ConfigExtensions.Contains(ext))
            return false;
        var name = NameStem(fileName);
        return ConfigFileNames.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Keeps only the graphics-relevant keys from a parsed INI.</summary>
    public static IReadOnlyList<ConfigSetting> ExtractGraphicsSettings(
        Dictionary<string, Dictionary<string, string>> ini)
    {
        var settings = new List<ConfigSetting>();
        foreach (var (section, keys) in ini)
            foreach (var (key, value) in keys)
                if (GraphicsKeywords.Any(k => key.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    settings.Add(new ConfigSetting(section, key, value));
        return settings;
    }

    private static void TryAdd(Dictionary<string, FoundConfig> results, string path)
    {
        if (results.ContainsKey(path))
            return;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var format = ext switch
        {
            ".json" => "Json",
            ".xml" => "Xml",
            ".ini" or ".cfg" or ".settings" => "Ini",
            _ => "Unknown",
        };

        IReadOnlyList<ConfigSetting> graphics = [];
        var total = 0;
        if (format == "Ini")
        {
            try
            {
                var ini = IniFile.Parse(File.ReadAllText(path));
                total = ini.Sum(s => s.Value.Count);
                graphics = ExtractGraphicsSettings(ini);
            }
            catch (IOException) { }
        }

        results[path] = new FoundConfig
        {
            Path = path, Format = format, GraphicsSettings = graphics, TotalKeys = total,
        };
    }

    private static IEnumerable<string> EnumerateInstallDirConfigs(string installDir)
    {
        if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir))
            yield break;

        var inspected = 0;
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((installDir, 0));
        while (queue.Count > 0 && inspected < MaxFilesInspected)
        {
            var (dir, depth) = queue.Dequeue();
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { continue; }

            foreach (var file in files)
            {
                if (++inspected >= MaxFilesInspected)
                    break;
                if (LooksLikeConfigFile(Path.GetFileName(file)))
                    yield return file;
            }

            if (depth < MaxInstallDirDepth)
            {
                string[] subdirs;
                try { subdirs = Directory.GetDirectories(dir); }
                catch { continue; }
                foreach (var sub in subdirs)
                    queue.Enqueue((sub, depth + 1));
            }
        }
    }

    private static IEnumerable<string> EnumerateMatchingUserConfigs(string root, string gameName)
    {
        if (!Directory.Exists(root))
            yield break;
        string[] folders;
        try { folders = Directory.GetDirectories(root); }
        catch { yield break; }

        foreach (var folder in folders)
        {
            if (!FolderMatchesGame(Path.GetFileName(folder), gameName))
                continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                    .Take(200);
            }
            catch { continue; }
            foreach (var file in files)
                if (LooksLikeConfigFile(Path.GetFileName(file)))
                    yield return file;
        }
    }

    private static IEnumerable<string> UserConfigRoots()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var roots = new[]
        {
            Path.Combine(documents, "My Games"),
            documents,
            localAppData,
            appData,
            localAppData + "Low", // %LOCALAPPDATA%Low, used by many games
        };
        return roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)).Distinct();
    }

    private static string Normalize(string s) =>
        new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string NameStem(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName);
}
