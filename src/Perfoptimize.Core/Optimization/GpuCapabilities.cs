using System.Text.RegularExpressions;
using Perfoptimize.Core.Hardware;

namespace Perfoptimize.Core.Optimization;

/// <summary>
/// What a given GPU can actually do — advice is only relevant if it never
/// suggests DLSS to a GTX or Radeon owner. Identifiers used in game profiles:
/// "dlss", "fsr", "xess" plus "dlss-fg" / "fsr-fg" for frame generation.
/// </summary>
public static partial class GpuCapabilities
{
    /// <summary>DLSS requires an NVIDIA RTX card (Tensor cores).</summary>
    public static bool SupportsDlss(GpuInfo gpu) =>
        gpu.Vendor.Equals("NVIDIA", StringComparison.OrdinalIgnoreCase) &&
        gpu.Name.Contains("RTX", StringComparison.OrdinalIgnoreCase);

    /// <summary>DLSS Frame Generation requires an RTX 40 series or newer.</summary>
    public static bool SupportsDlssFrameGeneration(GpuInfo gpu) =>
        SupportsDlss(gpu) && RtxModelNumber(gpu.Name) >= 4000;

    /// <summary>
    /// FSR runs on any reasonably modern GPU, and FSR 3 frame generation is
    /// vendor-agnostic; requiring a discrete card keeps expectations honest.
    /// </summary>
    public static bool SupportsFsrFrameGeneration(GpuInfo gpu) => gpu.IsDiscrete;

    public static bool IsIntelArc(GpuInfo gpu) =>
        gpu.Vendor.Equals("Intel", StringComparison.OrdinalIgnoreCase) &&
        gpu.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The best upscaler for this GPU among those the game actually ships.
    /// Order of preference: the vendor's own technology first (best image
    /// quality on that hardware), then XeSS/FSR which run everywhere.
    /// </summary>
    public static string? PreferredUpscaler(GpuInfo gpu, IReadOnlyList<string> gameUpscalers)
    {
        bool GameHas(string id) =>
            gameUpscalers.Any(u => u.Equals(id, StringComparison.OrdinalIgnoreCase));

        if (SupportsDlss(gpu) && GameHas("dlss"))
            return "DLSS";
        if (IsIntelArc(gpu) && GameHas("xess"))
            return "XeSS";
        if (GameHas("fsr"))
            return "FSR";
        if (GameHas("xess"))
            return "XeSS"; // XeSS has a fallback path that runs on any GPU.
        return null;
    }

    /// <summary>Frame generation flavor available to this GPU in this game, or null.</summary>
    public static string? AvailableFrameGeneration(GpuInfo gpu, IReadOnlyList<string> gameUpscalers)
    {
        bool GameHas(string id) =>
            gameUpscalers.Any(u => u.Equals(id, StringComparison.OrdinalIgnoreCase));

        if (SupportsDlssFrameGeneration(gpu) && GameHas("dlss-fg"))
            return "DLSS Frame Generation";
        if (SupportsFsrFrameGeneration(gpu) && GameHas("fsr-fg"))
            return "FSR Frame Generation";
        return null;
    }

    /// <summary>"NVIDIA GeForce RTX 4070 Ti" → 4070; null when no RTX model is found.</summary>
    public static int? RtxModelNumber(string gpuName)
    {
        var match = RtxModelRegex().Match(gpuName);
        return match.Success ? int.Parse(match.Groups["model"].Value) : null;
    }

    [GeneratedRegex(@"RTX\s*(?<model>\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex RtxModelRegex();
}
