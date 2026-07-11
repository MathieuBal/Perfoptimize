using System.Globalization;

namespace Perfoptimize.Core.Formatting;

public static class ByteSize
{
    private static readonly string[] Units = ["B", "KiB", "MiB", "GiB", "TiB"];

    /// <summary>Formats a byte count for display, e.g. 17179869184 → "16 GiB".</summary>
    public static string Humanize(long bytes)
    {
        if (bytes < 0) return "?";
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var rounded = Math.Round(value, 1);
        var text = rounded == Math.Floor(rounded)
            ? ((long)rounded).ToString(CultureInfo.InvariantCulture)
            : rounded.ToString("0.0", CultureInfo.InvariantCulture);
        return $"{text} {Units[unit]}";
    }
}
