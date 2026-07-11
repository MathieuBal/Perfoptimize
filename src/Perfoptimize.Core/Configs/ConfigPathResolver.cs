namespace Perfoptimize.Core.Configs;

/// <summary>
/// Expands the placeholders used in game profile config paths, e.g.
/// "%DOCUMENTS%\The Witcher 3\user.settings". Placeholders map to
/// <see cref="Environment.SpecialFolder"/> so profiles stay portable across
/// machines (and, for development, across operating systems).
/// </summary>
public static class ConfigPathResolver
{
    /// <summary>Expands placeholders using the current user's real folders.</summary>
    public static string Resolve(string template) => Resolve(template, DefaultLookup);

    /// <summary>Testable overload: the folder lookup is injected.</summary>
    public static string Resolve(string template, Func<string, string?> lookup)
    {
        var result = template;
        var start = result.IndexOf('%');
        while (start >= 0)
        {
            var end = result.IndexOf('%', start + 1);
            if (end < 0)
                break;
            var token = result[(start + 1)..end];
            var replacement = lookup(token);
            if (replacement is not null)
            {
                result = result[..start] + replacement + result[(end + 1)..];
                start = result.IndexOf('%', start + replacement.Length);
            }
            else
            {
                start = result.IndexOf('%', end + 1);
            }
        }

        if (result.StartsWith("~/", StringComparison.Ordinal) || result == "~")
            result = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + result[1..];

        // Profiles are written with Windows separators; normalize for the host OS.
        return result.Replace('\\', Path.DirectorySeparatorChar);
    }

    private static string? DefaultLookup(string token) => token.ToUpperInvariant() switch
    {
        "LOCALAPPDATA" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "APPDATA" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "USERPROFILE" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "DOCUMENTS" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        _ => Environment.GetEnvironmentVariable(token),
    };
}
