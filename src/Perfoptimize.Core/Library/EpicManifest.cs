using System.Text.Json;

namespace Perfoptimize.Core.Library;

/// <summary>One installed game from an Epic Games Launcher manifest (.item).</summary>
public readonly record struct EpicManifest(string DisplayName, string InstallLocation)
{
    /// <summary>Parses an Epic .item manifest (JSON); null when it lacks the fields.</summary>
    public static EpicManifest? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var name = root.TryGetProperty("DisplayName", out var n) ? n.GetString() : null;
            var location = root.TryGetProperty("InstallLocation", out var l) ? l.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(location))
                return null;
            return new EpicManifest(name!, location!);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
