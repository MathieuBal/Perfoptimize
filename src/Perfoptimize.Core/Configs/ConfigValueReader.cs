using System.Text.Json;
using System.Xml.Linq;

namespace Perfoptimize.Core.Configs;

/// <summary>
/// Reads the CURRENT values of a game's config file into a flat
/// key → value dictionary, so advisories can be evaluated against what the
/// player actually has set. Handles INI, JSON and XML; every key is exposed
/// under several aliases (bare key, section-qualified, dotted path) so an
/// advisory can reference it however is natural. Pure: text in, values out.
/// </summary>
public static class ConfigValueReader
{
    /// <summary>Parses config text of the given format into a case-insensitive value map.</summary>
    public static IReadOnlyDictionary<string, string> Read(string text, string format)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            switch (format.ToLowerInvariant())
            {
                case "ini": ReadIni(text, values); break;
                case "json": ReadJson(text, values); break;
                case "xml": ReadXml(text, values); break;
            }
        }
        catch
        {
            // A malformed config yields whatever was parsed so far, never a throw.
        }
        return values;
    }

    private static void ReadIni(string text, Dictionary<string, string> values)
    {
        foreach (var (section, keys) in IniFile.Parse(text))
            foreach (var (key, value) in keys)
            {
                Put(values, key, value);
                if (section.Length > 0)
                    Put(values, $"{section}.{key}", value);
            }
    }

    private static void ReadJson(string text, Dictionary<string, string> values)
    {
        using var doc = JsonDocument.Parse(text);
        Flatten(doc.RootElement, "", values);
    }

    private static void Flatten(JsonElement element, string path, Dictionary<string, string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var child = path.Length == 0 ? prop.Name : $"{path}.{prop.Name}";
                    Flatten(prop.Value, child, values);
                }
                break;
            case JsonValueKind.Array:
                // Arrays are rarely graphics settings; index them shallowly.
                var i = 0;
                foreach (var item in element.EnumerateArray())
                    Flatten(item, $"{path}[{i++}]", values);
                break;
            default:
                var value = element.ValueKind switch
                {
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => "",
                    _ => element.ToString(),
                };
                var leaf = path.Contains('.') ? path[(path.LastIndexOf('.') + 1)..] : path;
                Put(values, leaf, value);
                Put(values, path, value);
                break;
        }
    }

    private static void ReadXml(string text, Dictionary<string, string> values)
    {
        var root = XDocument.Parse(text).Root;
        if (root is not null)
            FlattenXml(root, values);
    }

    private static void FlattenXml(XElement element, Dictionary<string, string> values)
    {
        // GTA-style: <MSAA value="0" />. Expose element name -> value attribute,
        // element name -> inner text, and element.attr -> attribute value.
        var valueAttr = element.Attribute("value")?.Value;
        if (valueAttr is not null)
            Put(values, element.Name.LocalName, valueAttr);

        if (!element.HasElements)
        {
            var inner = element.Value.Trim();
            if (inner.Length > 0)
                Put(values, element.Name.LocalName, inner);
        }

        foreach (var attr in element.Attributes())
            Put(values, $"{element.Name.LocalName}.{attr.Name.LocalName}", attr.Value);

        foreach (var child in element.Elements())
            FlattenXml(child, values);
    }

    private static void Put(Dictionary<string, string> values, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(key))
            values[key] = value;
    }
}
