namespace Perfoptimize.Core.Configs;

/// <summary>
/// Minimal INI reader/editor for game configuration files
/// (user.settings, production_Config.ini, ...). Edits are line-based so the
/// file keeps its comments, ordering and formatting — game launchers sometimes
/// validate their own files, so we must stay as close to the original as possible.
/// </summary>
public static class IniFile
{
    /// <summary>
    /// Parses INI text into sections of key/values. Keys appearing before any
    /// section header land in the "" section. Comments (';' or '#') are skipped.
    /// </summary>
    public static Dictionary<string, Dictionary<string, string>> Parse(string text)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [""] = new(StringComparer.OrdinalIgnoreCase),
        };
        var current = sections[""];

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
                continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                var sectionName = line[1..^1].Trim();
                if (!sections.TryGetValue(sectionName, out current!))
                {
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    sections[sectionName] = current;
                }
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            current[key] = value;
        }
        return sections;
    }

    /// <summary>
    /// Returns a copy of the INI text where <paramref name="key"/> in
    /// <paramref name="section"/> has the new value, touching only that line.
    /// The key (and section) are appended when missing.
    /// </summary>
    public static string SetValue(string text, string section, string key, string value)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        var currentSection = "";
        var sectionEndIndex = -1; // last content line of the target section
        var sectionFound = section.Length == 0;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.Length > 1 && line[0] == '[' && line[^1] == ']')
            {
                currentSection = line[1..^1].Trim();
                if (currentSection.Equals(section, StringComparison.OrdinalIgnoreCase))
                {
                    sectionFound = true;
                    sectionEndIndex = i;
                }
                continue;
            }

            if (!currentSection.Equals(section, StringComparison.OrdinalIgnoreCase))
                continue;
            if (line.Length > 0 && line[0] is not (';' or '#'))
                sectionEndIndex = i;

            var separator = lines[i].IndexOf('=');
            if (separator <= 0)
                continue;
            if (lines[i][..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                // Preserve the original spacing around '=' by rebuilding minimally.
                var prefix = lines[i][..(separator + 1)];
                var rest = lines[i][(separator + 1)..];
                var spacing = rest[..(rest.Length - rest.TrimStart().Length)];
                lines[i] = $"{prefix}{spacing}{value}";
                return string.Join(newline, lines);
            }
        }

        // Key not found: append it to the section (creating the section if needed).
        if (!sectionFound)
        {
            if (lines.Count > 0 && lines[^1].Length > 0)
                lines.Add("");
            lines.Add($"[{section}]");
            lines.Add($"{key}={value}");
        }
        else
        {
            lines.Insert(sectionEndIndex + 1, $"{key}={value}");
        }
        return string.Join(newline, lines);
    }
}
