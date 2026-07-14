using System.Text.RegularExpressions;

namespace Perfoptimize.Core.Library;

/// <summary>One game installed through Steam, read from its app manifest.</summary>
public readonly record struct SteamApp(string AppId, string Name, string InstallDir);

/// <summary>
/// Pure parsers for Steam's Valve Data Format files: <c>libraryfolders.vdf</c>
/// (the list of library roots) and <c>appmanifest_*.acf</c> (one per installed
/// game). Text-in / data-out, so game discovery is unit-testable without a real
/// Steam install.
/// </summary>
public static partial class SteamVdf
{
    /// <summary>Extracts every library root path from libraryfolders.vdf.</summary>
    public static IReadOnlyList<string> ParseLibraryFolders(string vdfText)
    {
        var paths = new List<string>();
        foreach (Match m in PathRegex().Matches(vdfText))
        {
            // VDF escapes backslashes; turn "\\" back into a real separator.
            var path = m.Groups["path"].Value.Replace(@"\\", @"\");
            if (path.Length > 0)
                paths.Add(path);
        }
        return paths;
    }

    /// <summary>Parses one appmanifest_*.acf; null when it lacks a name or install dir.</summary>
    public static SteamApp? ParseAppManifest(string acfText)
    {
        var appId = Field(acfText, "appid");
        var name = Field(acfText, "name");
        var installDir = Field(acfText, "installdir");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDir))
            return null;
        return new SteamApp(appId ?? "", name!, installDir!);
    }

    private static string? Field(string vdf, string key)
    {
        var match = Regex.Match(vdf, $"\"{Regex.Escape(key)}\"\\s+\"(?<v>[^\"]*)\"",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["v"].Value : null;
    }

    [GeneratedRegex("\"path\"\\s+\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex PathRegex();
}
