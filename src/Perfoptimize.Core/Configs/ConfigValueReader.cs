using System.Text.Json;
using System.Xml.Linq;

namespace Perfoptimize.Core.Configs;

/// <summary>
/// Flattens INI, JSON and XML config text into a case-insensitive key/value map.
/// Qualified keys are always kept; bare aliases are kept only while unambiguous.
/// Malformed input is best-effort and never throws to callers.
/// </summary>
public static class ConfigValueReader
{
    public static IReadOnlyDictionary<string, string> Read(string text, string format)
    {
        var builder = new KeyMap();
        try
        {
            switch (format.ToLowerInvariant())
            {
                case "ini": ReadIni(text, builder); break;
                case "json": ReadJson(text, builder); break;
                case "xml": ReadXml(text, builder); break;
            }
        }
        catch
        {
            // A malformed file simply yields the values that were safe to parse.
        }
        return builder.Build();
    }

    private static void ReadIni(string text, KeyMap map)
    {
        foreach (var (section, keys) in IniFile.Parse(text))
        foreach (var (key, value) in keys)
        {
            if (section.Length > 0)
                map.PutQualified($"{section}.{key}", value);
            map.PutBare(key, value);
        }
    }

    private static void ReadJson(string text, KeyMap map)
    {
        using var doc = JsonDocument.Parse(text);
        FlattenJson(doc.RootElement, "", map);
    }

    private static void FlattenJson(JsonElement element, string path, KeyMap map)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                    FlattenJson(prop.Value, path.Length == 0 ? prop.Name : $"{path}.{prop.Name}", map);
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in element.EnumerateArray())
                    FlattenJson(item, $"{path}[{i++}]", map);
                break;
            default:
                var value = element.ValueKind switch
                {
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => "",
                    _ => element.ToString(),
                };
                if (path.Contains('.'))
                {
                    map.PutQualified(path, value);
                    map.PutBare(path[(path.LastIndexOf('.') + 1)..], value);
                }
                else
                {
                    map.PutBare(path, value);
                }
                break;
        }
    }

    private static void ReadXml(string text, KeyMap map)
    {
        var root = XDocument.Parse(text).Root;
        if (root is not null)
            FlattenXml(root, map);
    }

    private static void FlattenXml(XElement element, KeyMap map)
    {
        if (element.Attribute("value")?.Value is { } valueAttr)
            map.PutBare(element.Name.LocalName, valueAttr);

        if (!element.HasElements)
        {
            var inner = element.Value.Trim();
            if (inner.Length > 0)
                map.PutBare(element.Name.LocalName, inner);
        }

        foreach (var attr in element.Attributes())
            map.PutQualified($"{element.Name.LocalName}.{attr.Name.LocalName}", attr.Value);
        foreach (var child in element.Elements())
            FlattenXml(child, map);
    }

    private sealed class KeyMap
    {
        private readonly Dictionary<string, string> _qualified = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _bare = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ambiguousBare = new(StringComparer.OrdinalIgnoreCase);

        public void PutQualified(string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(key))
                _qualified[key] = value;
        }

        public void PutBare(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;
            if (_bare.TryGetValue(key, out var existing))
            {
                if (!string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
                    _ambiguousBare.Add(key);
            }
            else
            {
                _bare[key] = value;
            }
        }

        public IReadOnlyDictionary<string, string> Build()
        {
            var result = new Dictionary<string, string>(_qualified, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in _bare)
                if (!_ambiguousBare.Contains(key) && !result.ContainsKey(key))
                    result[key] = value;
            return result;
        }
    }
}
