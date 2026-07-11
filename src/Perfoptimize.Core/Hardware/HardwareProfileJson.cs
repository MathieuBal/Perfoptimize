using System.Text.Json;
using System.Text.Json.Serialization;

namespace Perfoptimize.Core.Hardware;

/// <summary>JSON persistence for hardware profiles (saved locally, shareable for support).</summary>
public static class HardwareProfileJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(HardwareProfile profile) =>
        JsonSerializer.Serialize(profile, Options);

    public static HardwareProfile? Deserialize(string json) =>
        JsonSerializer.Deserialize<HardwareProfile>(json, Options);
}
