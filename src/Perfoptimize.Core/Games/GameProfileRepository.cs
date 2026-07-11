using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Perfoptimize.Core.Games;

/// <summary>
/// Loads game profiles: the built-in ones embedded in this assembly
/// (Games/Profiles/*.json) plus optional user-provided JSON files.
/// </summary>
public static class GameProfileRepository
{
    private const string ResourcePrefix = "Perfoptimize.Core.Games.Profiles.";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static IReadOnlyList<GameProfile> LoadBuiltIn()
    {
        var assembly = typeof(GameProfileRepository).Assembly;
        var profiles = new List<GameProfile>();
        foreach (var resourceName in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) &&
                                 n.EndsWith(".json", StringComparison.Ordinal))
                     .OrderBy(n => n))
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                continue;
            using var reader = new StreamReader(stream);
            if (Parse(reader.ReadToEnd()) is { } profile)
                profiles.Add(profile);
        }
        return profiles;
    }

    /// <summary>Loads extra profiles from a user directory; unreadable files are skipped.</summary>
    public static IReadOnlyList<GameProfile> LoadFromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return [];
        var profiles = new List<GameProfile>();
        foreach (var file in Directory.GetFiles(directory, "*.json").OrderBy(f => f))
        {
            try
            {
                if (Parse(File.ReadAllText(file)) is { } profile)
                    profiles.Add(profile);
            }
            catch (IOException)
            {
            }
        }
        return profiles;
    }

    public static GameProfile? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<GameProfile>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
