using System.Text.Json;
using System.Xml.Linq;

namespace Perfoptimize.Core.Configs;

/// <summary>
/// Reads the CURRENT values of a game's config file into a flat
/// key → value dictionary, so advisories can be evaluated against what the
/// player actually has set. Handles INI, JSON and XML.
///
/// Every value is exposed under a FULLY-QUALIFIED key ("section.key", dotted
/// JSON path, "element.attr") that can never collide, plus a convenience BARE
/// key ("key") — but the bare key is dropped when it is ambiguous (the same
/// short name carries different values in two sections/paths). This is
/// deliberate: a bare key that silently returned "the last one wins" would make
/// an advisory read the wrong setting, so a serious advisory targets the
/// qualified "section.key" and the bare alias only survives when unambiguous.
/// Pure: text in, values out; never throws.
/// </summary>
public static class ConfigValueReader
{
    /// <summary>Parses config text of the given format into a case-insensitive value map.</summary>
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
            // A malformed config yields whatever was parsed so far, never a throw.
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
        Flatten(doc.RootElement, "", map);
    }

    private static void Flatten(JsonElement element, string path, KeyMap map)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var child = path.Length == 0 ? prop.Name : $"{path}.{prop.Name}";
                    Flatten(prop.Value, child, map);
                }
                break;
            case JsonValueKind.Array:
                // Arrays are rarely graphics settings; index them shallowly.
                var i = 0;
                foreach (var item in element.EnumerateArray())
                    Flatten(item, $"{path}[{i++}]", map);
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
        // GTA-style: <MSAA value="0" />. Expose element name -> value attribute,
        // element name -> inner text, and element.attr -> attribute value.
        var valueAttr = element.Attribute("value")?.Value;
        if (valueAttr is not null)
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

    /// <summary>
    /// Accumulates qualified and bare keys, then emits a map where bare keys that
    /// turned out ambiguous (conflicting values) are omitted in favour of their
    /// safe qualified forms.
    /// </summary>
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
                // Same short name, different value in another section/path → the
                // bare alias is no longer trustworthy; force qualification.
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
