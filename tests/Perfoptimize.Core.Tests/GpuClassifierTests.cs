using Perfoptimize.Core.Hardware;

namespace Perfoptimize.Core.Tests;

public class GpuClassifierTests
{
    [Theory]
    [InlineData("NVIDIA", "NVIDIA GeForce RTX 3060", "NVIDIA")]
    [InlineData("nVidia Corporation", "GA106", "NVIDIA")]
    [InlineData(null, "GeForce GTX 1080", "NVIDIA")]
    [InlineData("Advanced Micro Devices, Inc.", "Radeon RX 6700 XT", "AMD")]
    [InlineData("ATI Technologies Inc.", "Radeon HD 5870", "AMD")]
    [InlineData("Intel Corporation", "Intel(R) UHD Graphics 630", "Intel")]
    [InlineData("Microsoft", "Microsoft Basic Display Adapter", "Microsoft")]
    [InlineData("Matrox", "G200", "Matrox")]
    [InlineData("", "Something exotic", "Unknown")]
    public void NormalizeVendor_recognizes_common_spellings(string? raw, string name, string expected)
    {
        Assert.Equal(expected, GpuClassifier.NormalizeVendor(raw, name));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 3060", "NVIDIA", true)]
    [InlineData("NVIDIA GeForce GTX 1650 Laptop GPU", "NVIDIA", true)]
    [InlineData("AMD Radeon RX 6700 XT", "AMD", true)]
    [InlineData("AMD Radeon R9 390", "AMD", true)]
    [InlineData("AMD Radeon VII", "AMD", true)]
    [InlineData("AMD Radeon(TM) Graphics", "AMD", false)]         // Ryzen APU iGPU
    [InlineData("AMD Radeon(TM) Vega 8 Graphics", "AMD", false)]  // APU iGPU
    [InlineData("Intel(R) UHD Graphics 630", "Intel", false)]
    [InlineData("Intel(R) Iris(R) Xe Graphics", "Intel", false)]
    [InlineData("Intel(R) Arc(TM) A750 Graphics", "Intel", true)]
    [InlineData("Microsoft Basic Display Adapter", "Microsoft", false)]
    public void IsDiscrete_separates_dedicated_cards_from_integrated(string name, string vendor, bool expected)
    {
        Assert.Equal(expected, GpuClassifier.IsDiscrete(name, vendor));
    }
}
