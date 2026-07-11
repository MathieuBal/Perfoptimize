using Perfoptimize.Core.Hardware;
using Perfoptimize.Core.Hardware.Linux;

namespace Perfoptimize.Core.Tests;

public class LinuxProcParserTests
{
    [Fact]
    public void ParseCpuInfo_reads_name_vendor_and_core_counts()
    {
        // 2 physical cores, each with 2 hyper-threads (4 logical processors).
        const string cpuinfo = """
            processor	: 0
            vendor_id	: AuthenticAMD
            model name	: AMD Ryzen 5 5600X 6-Core Processor
            physical id	: 0
            core id		: 0
            cpu cores	: 2

            processor	: 1
            vendor_id	: AuthenticAMD
            model name	: AMD Ryzen 5 5600X 6-Core Processor
            physical id	: 0
            core id		: 0
            cpu cores	: 2

            processor	: 2
            vendor_id	: AuthenticAMD
            model name	: AMD Ryzen 5 5600X 6-Core Processor
            physical id	: 0
            core id		: 1
            cpu cores	: 2

            processor	: 3
            vendor_id	: AuthenticAMD
            model name	: AMD Ryzen 5 5600X 6-Core Processor
            physical id	: 0
            core id		: 1
            cpu cores	: 2
            """;

        var cpu = LinuxProcParser.ParseCpuInfo(cpuinfo);

        Assert.Equal("AMD Ryzen 5 5600X 6-Core Processor", cpu.Name);
        Assert.Equal("AMD", cpu.Vendor);
        Assert.Equal(2, cpu.PhysicalCores);
        Assert.Equal(4, cpu.LogicalCores);
    }

    [Fact]
    public void ParseCpuInfo_handles_missing_topology_fields()
    {
        const string cpuinfo = """
            processor	: 0
            vendor_id	: GenuineIntel
            model name	: Intel(R) Core(TM) i5-9400F

            processor	: 1
            vendor_id	: GenuineIntel
            model name	: Intel(R) Core(TM) i5-9400F
            """;

        var cpu = LinuxProcParser.ParseCpuInfo(cpuinfo);

        Assert.Equal("Intel", cpu.Vendor);
        Assert.Equal(2, cpu.LogicalCores);
        Assert.Equal(2, cpu.PhysicalCores); // Falls back to the logical count.
    }

    [Fact]
    public void ParseMemInfo_converts_kilobytes_to_bytes()
    {
        const string meminfo = """
            MemTotal:       16305356 kB
            MemFree:          602616 kB
            MemAvailable:    9333852 kB
            Buffers:          928556 kB
            """;

        var memory = LinuxProcParser.ParseMemInfo(meminfo);

        Assert.Equal(16305356L * 1024, memory.TotalBytes);
        Assert.Equal(9333852L * 1024, memory.AvailableBytes);
    }

    [Fact]
    public void ParseLspci_keeps_display_controllers_and_extracts_marketing_names()
    {
        const string lspci = """
            00:00.0 "Host bridge" "Intel Corporation" "8th Gen Core Processor Host Bridge" -r07 "" ""
            00:02.0 "VGA compatible controller" "Intel Corporation" "CoffeeLake-S GT2 [UHD Graphics 630]" "" ""
            01:00.0 "VGA compatible controller" "NVIDIA Corporation" "GA106 [GeForce RTX 3060]" -ra1 "" ""
            02:00.0 "Ethernet controller" "Realtek" "RTL8111" "" ""
            """;

        var gpus = LinuxProcParser.ParseLspci(lspci);

        Assert.Equal(2, gpus.Count);
        Assert.Equal("UHD Graphics 630", gpus[0].Name);
        Assert.Equal("Intel", gpus[0].Vendor);
        Assert.False(gpus[0].IsDiscrete);
        Assert.Equal("GeForce RTX 3060", gpus[1].Name);
        Assert.Equal("NVIDIA", gpus[1].Vendor);
        Assert.True(gpus[1].IsDiscrete);
    }

    [Fact]
    public void ParseXrandr_reads_connected_outputs_with_active_refresh_rate()
    {
        const string xrandr = """
            Screen 0: minimum 320 x 200, current 4480 x 1440, maximum 16384 x 16384
            DP-1 connected primary 2560x1440+0+0 (normal left inverted) 597mm x 336mm
               2560x1440    143.91*+  59.95
               1920x1080     60.00
            HDMI-1 connected 1920x1080+2560+0 (normal left inverted) 527mm x 296mm
               1920x1080     60.00*+  50.00
            DP-2 disconnected (normal left inverted right x axis y axis)
            """;

        var displays = LinuxProcParser.ParseXrandr(xrandr);

        Assert.Equal(2, displays.Count);
        Assert.Equal("DP-1", displays[0].Name);
        Assert.True(displays[0].IsPrimary);
        Assert.Equal(2560, displays[0].WidthPixels);
        Assert.Equal(1440, displays[0].HeightPixels);
        Assert.Equal(144, displays[0].RefreshRateHz);
        Assert.Equal("HDMI-1", displays[1].Name);
        Assert.False(displays[1].IsPrimary);
        Assert.Equal(60, displays[1].RefreshRateHz);
    }

    [Fact]
    public void ParseOsRelease_reads_pretty_name_and_version()
    {
        const string osRelease = """
            PRETTY_NAME="Ubuntu 24.04.2 LTS"
            NAME="Ubuntu"
            VERSION_ID="24.04"
            """;

        var os = LinuxProcParser.ParseOsRelease(osRelease, "X64");

        Assert.Equal("Ubuntu 24.04.2 LTS", os.Name);
        Assert.Equal("24.04", os.Version);
        Assert.Equal("X64", os.Architecture);
    }
}
