using Perfoptimize.Core.Hardware;

namespace Perfoptimize.Core.Hardware.Windows;

/// <summary>
/// Pure conversions of raw WMI values, kept platform-neutral so they can be
/// unit-tested on any OS.
/// </summary>
public static class WindowsParsing
{
    /// <summary>
    /// Decodes a WMI monitor string (WmiMonitorID.UserFriendlyName): an array of
    /// UTF-16 code units padded with trailing zeros.
    /// </summary>
    public static string? DecodeMonitorName(ushort[]? codeUnits)
    {
        if (codeUnits is null || codeUnits.Length == 0)
            return null;
        var chars = codeUnits.TakeWhile(c => c != 0).Select(c => (char)c).ToArray();
        var name = new string(chars).Trim();
        return name.Length == 0 ? null : name;
    }

    /// <summary>Maps MSFT_PhysicalDisk.MediaType to our media type enum.</summary>
    public static StorageMediaType MapMsftMediaType(int mediaType) => mediaType switch
    {
        3 => StorageMediaType.Hdd,
        4 => StorageMediaType.Ssd,
        5 => StorageMediaType.Scm,
        _ => StorageMediaType.Unknown,
    };
}
