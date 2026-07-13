using System.Management;
using System.Runtime.Versioning;

namespace Perfoptimize.Core.Hardware.Windows;

/// <summary>Small helper to run WQL queries and materialize results as dictionaries.</summary>
[SupportedOSPlatform("windows")]
internal static class WmiQuery
{
    /// <summary>
    /// Per-query wall-clock cap. The performance-counter classes
    /// (GPUEngine, PerfProc_Process, ...) can take tens of seconds on their
    /// first cold access on a real machine; without a bound, one slow query
    /// eats an entire monitoring session. Semi-synchronous enumeration
    /// (ReturnImmediately) is what makes the timeout actually apply.
    /// </summary>
    // Fully qualified: System.IO also defines an EnumerationOptions type.
    private static readonly System.Management.EnumerationOptions BoundedEnumeration = new()
    {
        Timeout = TimeSpan.FromSeconds(3),
        ReturnImmediately = true,
        Rewindable = false,
    };

    /// <summary>Runs a WQL query; failures or timeouts yield an empty list.</summary>
    public static List<Dictionary<string, object?>> Run(string wql, string scope = @"root\cimv2")
    {
        var rows = new List<Dictionary<string, object?>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(scope), new ObjectQuery(wql), BoundedEnumeration);
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
        catch (TimeoutException)
        {
            // Query exceeded the cap; return whatever was collected so far.
        }
        return rows;
    }

    public static int? ToInt(object? value) => value is null ? null : Convert.ToInt32(value);

    public static long? ToLong(object? value) => value is null ? null : Convert.ToInt64(value);

    public static double? ToDouble(object? value) => value is null ? null : Convert.ToDouble(value);
}
