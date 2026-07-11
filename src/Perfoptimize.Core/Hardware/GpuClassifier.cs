namespace Perfoptimize.Core.Hardware;

/// <summary>
/// Heuristics shared by all platforms to interpret raw GPU strings.
/// Kept pure so the rules are unit-testable without real hardware.
/// </summary>
public static class GpuClassifier
{
    /// <summary>Normalizes the many vendor spellings found in WMI/PCI data.</summary>
    public static string NormalizeVendor(string? rawVendor, string? gpuName = null)
    {
        var source = $"{rawVendor} {gpuName}";
        if (source.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
            return "NVIDIA";
        if (source.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("ATI ", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
            return "AMD";
        if (source.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            return "Intel";
        if (source.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
            return "Microsoft";
        return string.IsNullOrWhiteSpace(rawVendor) ? "Unknown" : rawVendor.Trim();
    }

    /// <summary>
    /// Guesses whether a GPU is a dedicated card or an integrated one.
    /// Integrated examples: "Intel(R) UHD Graphics 630", "AMD Radeon(TM) Graphics" (APU).
    /// Discrete examples: "NVIDIA GeForce RTX 3060", "AMD Radeon RX 6700 XT", "Intel Arc A750".
    /// </summary>
    public static bool IsDiscrete(string name, string vendor)
    {
        if (vendor.Equals("Microsoft", StringComparison.OrdinalIgnoreCase))
            return false; // "Microsoft Basic Display Adapter" and other virtual adapters.

        if (vendor.Equals("NVIDIA", StringComparison.OrdinalIgnoreCase))
            return true;

        if (vendor.Equals("Intel", StringComparison.OrdinalIgnoreCase))
            return name.Contains("Arc", StringComparison.OrdinalIgnoreCase);

        if (vendor.Equals("AMD", StringComparison.OrdinalIgnoreCase))
        {
            // Discrete Radeon cards carry a product line ("RX 6700 XT", "R9 390", "Pro W6800").
            // APU iGPUs are reported as bare "AMD Radeon(TM) Graphics" or "Radeon Vega 8".
            if (name.Contains("Vega", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("Radeon VII", StringComparison.OrdinalIgnoreCase))
                return false;
            return name.Contains(" RX", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains(" R9", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains(" R7", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains(" HD ", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Radeon VII", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Pro W", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("FirePro", StringComparison.OrdinalIgnoreCase);
        }

        // Unknown vendor: assume integrated so we never over-promise performance.
        return false;
    }
}
