using System.Management;
using System.Runtime.Versioning;

namespace Perfoptimize.Core.Hardware.Windows;

/// <summary>Small helper to run WQL queries and materialize results as dictionaries.</summary>
[SupportedOSPlatform("windows")]
internal static class WmiQuery
{
    /// <summary>Runs a WQL query; failures yield an empty list.</summary>
    public static List<Dictionary<string, object?>> Run(string wql, string scope = @"root\cimv2")
    {
        var rows = new List<Dictionary<string, object?>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, wql);
            using var results = searcher.Get();
            foreach (var item in results)
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in item.Properties)
                    row[property.Name] = property.Value;
                rows.Add(row);
                item.Dispose();
            }
        }
        catch (ManagementException)
        {
            // Class or namespace unavailable on this edition of Windows.
        }
        return rows;
    }

    public static int? ToInt(object? value) => value is null ? null : Convert.ToInt32(value);

    public static long? ToLong(object? value) => value is null ? null : Convert.ToInt64(value);

    public static double? ToDouble(object? value) => value is null ? null : Convert.ToDouble(value);
}
