using Perfoptimize.Core.Formatting;
using Perfoptimize.Core.Hardware;

namespace Perfoptimize.Core.Tests;

public class HardwareProfileTests
{
    [Fact]
    public void PrimaryGpu_prefers_discrete_card_over_integrated()
    {
        var profile = new HardwareProfile
        {
            Gpus =
            [
                new GpuInfo { Name = "Intel(R) UHD Graphics 630", Vendor = "Intel", IsDiscrete = false, VramBytes = 128L << 20 },
                new GpuInfo { Name = "NVIDIA GeForce RTX 3060", Vendor = "NVIDIA", IsDiscrete = true, VramBytes = 12L << 30 },
            ],
        };

        Assert.Equal("NVIDIA GeForce RTX 3060", profile.PrimaryGpu?.Name);
    }

    [Fact]
    public void PrimaryGpu_is_null_when_no_gpu_detected()
    {
        Assert.Null(new HardwareProfile().PrimaryGpu);
    }

    [Fact]
    public void Json_round_trip_preserves_the_profile()
    {
        var profile = new HardwareProfile
        {
            Cpu = new CpuInfo { Name = "AMD Ryzen 5 5600X", Vendor = "AMD", PhysicalCores = 6, LogicalCores = 12, MaxClockMhz = 3700 },
            Gpus = [new GpuInfo { Name = "NVIDIA GeForce RTX 3060", Vendor = "NVIDIA", VramBytes = 12L << 30, IsDiscrete = true }],
            Memory = new MemoryInfo { TotalBytes = 16L << 30, SpeedMtPerSecond = 3200, ModuleCount = 2 },
            Displays = [new DisplayInfo { Name = "DELL S2721DGF", WidthPixels = 2560, HeightPixels = 1440, RefreshRateHz = 144, IsPrimary = true }],
            StorageDevices = [new StorageDeviceInfo { Model = "Samsung SSD 980", MediaType = StorageMediaType.Ssd, TotalBytes = 1L << 40 }],
            Volumes = [new VolumeInfo { MountPoint = @"C:\", TotalBytes = 1L << 40, FreeBytes = 300L << 30 }],
            OperatingSystem = new OperatingSystemInfo { Name = "Windows 11 Pro", Version = "10.0.22631", Architecture = "X64" },
        };

        var restored = HardwareProfileJson.Deserialize(HardwareProfileJson.Serialize(profile));

        Assert.NotNull(restored);
        Assert.Equal(profile.Cpu.Name, restored.Cpu.Name);
        Assert.Equal(profile.Gpus[0].VramBytes, restored.Gpus[0].VramBytes);
        Assert.Equal(StorageMediaType.Ssd, restored.StorageDevices[0].MediaType);
        Assert.Equal(profile.Displays[0].RefreshRateHz, restored.Displays[0].RefreshRateHz);
        Assert.Equal(profile.Volumes[0].FreeBytes, restored.Volumes[0].FreeBytes);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KiB")]
    [InlineData(1536, "1.5 KiB")]
    [InlineData(12L << 30, "12 GiB")]
    [InlineData(16305356L * 1024, "15.5 GiB")]
    [InlineData(-1, "?")]
    public void ByteSize_humanizes_counts(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSize.Humanize(bytes));
    }
}
