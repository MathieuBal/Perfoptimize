using System.Reflection;

namespace Perfoptimize.App;

/// <summary>
/// Reads the dashboard's HTML/CSS/JS from embedded resources so the whole UI
/// ships inside a single-file executable with no loose files to lose.
/// </summary>
internal static class WebAssets
{
    private const string Prefix = "Perfoptimize.App.WebAssets.";

    public static string Read(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(Prefix + fileName)
            ?? throw new FileNotFoundException($"Embedded UI asset '{fileName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
