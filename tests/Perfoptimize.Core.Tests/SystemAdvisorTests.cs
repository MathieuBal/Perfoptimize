using Perfoptimize.Core.Hardware;
using Perfoptimize.Core.Optimization;

namespace Perfoptimize.Core.Tests;

public class SystemAdvisorTests
{
    private const long GiB = 1024L * 1024 * 1024;

    private static HardwareProfile Profile(
        int? ramModules = 2, long ramBytes = 32 * GiB, int? ramSpeed = 3600,
        StorageMediaType[]? storage = null, (string mount, long free, long total)[]? volumes = null,
        bool discreteGpu = true)
    {
        return new HardwareProfile
        {
            Memory = new MemoryInfo { TotalBytes = ramBytes, ModuleCount = ramModules, SpeedMtPerSecond = ramSpeed },
            StorageDevices = (storage ?? [StorageMediaType.Ssd])
                .Select(m => new StorageDeviceInfo { MediaType = m, TotalBytes = GiB }).ToList(),
            Volumes = (volumes ?? [("C:\\", 300 * GiB, 500 * GiB)])
                .Select(v => new VolumeInfo { MountPoint = v.mount, FreeBytes = v.free, TotalBytes = v.total }).ToList(),
            Gpus = discreteGpu
                ? [new GpuInfo { Name = "NVIDIA GeForce RTX 3060", Vendor = "NVIDIA", IsDiscrete = true }]
                : [new GpuInfo { Name = "Intel UHD 630", Vendor = "Intel", IsDiscrete = false }],
        };
    }

    [Fact]
    public void A_healthy_machine_gets_no_warnings()
    {
        var advice = SystemAdvisor.Analyze(Profile());

        Assert.DoesNotContain(advice, a => a.Severity == SystemAdviceSeverity.Warning);
    }

    [Fact]
    public void Single_channel_ram_is_a_warning()
    {
        var advice = SystemAdvisor.Analyze(Profile(ramModules: 1));

        Assert.Contains(advice, a => a.Title.Contains("simple canal") && a.Severity == SystemAdviceSeverity.Warning);
    }

    [Fact]
    public void Sixteen_gib_ram_is_suggested_to_upgrade()
    {
        var advice = SystemAdvisor.Analyze(Profile(ramBytes: 16 * GiB));

        Assert.Contains(advice, a => a.Category == "Mémoire" && a.Title.Contains("16 Go"));
    }

    [Fact]
    public void Slow_ram_with_multiple_sticks_hints_at_xmp()
    {
        var advice = SystemAdvisor.Analyze(Profile(ramSpeed: 2400, ramModules: 2));

        Assert.Contains(advice, a => a.Title.Contains("XMP"));
    }

    [Fact]
    public void A_nearly_full_system_drive_is_a_warning()
    {
        var advice = SystemAdvisor.Analyze(Profile(
            volumes: [("C:\\", 12 * GiB, 500 * GiB)])); // 12 GiB free, ~2%

        Assert.Contains(advice, a => a.Title.Contains("C:\\") && a.Severity == SystemAdviceSeverity.Warning);
    }

    [Fact]
    public void Tiny_system_partitions_are_not_flagged_as_full()
    {
        // A 500 MiB EFI partition at 0% free must not produce a warning.
        var advice = SystemAdvisor.Analyze(Profile(
            volumes: [("\\\\?\\Volume{efi}", 0, 500L * 1024 * 1024)]));

        Assert.DoesNotContain(advice, a => a.Category == "Stockage" && a.Title.Contains("plein"));
    }

    [Fact]
    public void No_ssd_is_suggested_to_add_one()
    {
        var advice = SystemAdvisor.Analyze(Profile(storage: [StorageMediaType.Hdd, StorageMediaType.Hdd]));

        Assert.Contains(advice, a => a.Title.Contains("SSD"));
    }

    [Fact]
    public void No_discrete_gpu_is_a_warning()
    {
        var advice = SystemAdvisor.Analyze(Profile(discreteGpu: false));

        Assert.Contains(advice, a => a.Category == "Carte graphique" && a.Severity == SystemAdviceSeverity.Warning);
    }

    [Fact]
    public void Findings_are_ordered_most_severe_first()
    {
        var advice = SystemAdvisor.Analyze(Profile(ramModules: 1, ramBytes: 16 * GiB));

        // Single-channel (Warning) must come before the 16 GiB upgrade (Suggestion).
        var warningIndex = advice.ToList().FindIndex(a => a.Severity == SystemAdviceSeverity.Warning);
        var suggestionIndex = advice.ToList().FindIndex(a => a.Severity == SystemAdviceSeverity.Suggestion);
        Assert.True(warningIndex >= 0 && warningIndex < suggestionIndex);
    }
}
