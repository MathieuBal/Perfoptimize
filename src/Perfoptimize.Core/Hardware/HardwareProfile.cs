namespace Perfoptimize.Core.Hardware;

/// <summary>
/// Complete hardware profile of the machine, produced by <see cref="HardwareScanner"/>.
/// This is the foundation used later by the recommendation engine.
/// </summary>
public sealed class HardwareProfile
{
    public DateTimeOffset ScannedAt { get; init; } = DateTimeOffset.Now;
    public CpuInfo Cpu { get; init; } = new();
    public IReadOnlyList<GpuInfo> Gpus { get; init; } = [];
    public MemoryInfo Memory { get; init; } = new();
    public IReadOnlyList<DisplayInfo> Displays { get; init; } = [];
    public IReadOnlyList<StorageDeviceInfo> StorageDevices { get; init; } = [];
    public IReadOnlyList<VolumeInfo> Volumes { get; init; } = [];
    public OperatingSystemInfo OperatingSystem { get; init; } = new();

    /// <summary>The GPU most likely used for gaming (discrete first, then largest VRAM).</summary>
    public GpuInfo? PrimaryGpu =>
        Gpus.OrderByDescending(g => g.IsDiscrete).ThenByDescending(g => g.VramBytes ?? 0).FirstOrDefault();
}

public sealed class CpuInfo
{
    public string Name { get; init; } = "Unknown";
    public string Vendor { get; init; } = "Unknown";
    public int PhysicalCores { get; init; }
    public int LogicalCores { get; init; }
    /// <summary>Base/max clock reported by the system, in MHz. Null when unavailable.</summary>
    public int? MaxClockMhz { get; init; }
}

public sealed class GpuInfo
{
    public string Name { get; init; } = "Unknown";
    public string Vendor { get; init; } = "Unknown";
    /// <summary>Dedicated video memory in bytes. Null when it could not be determined.</summary>
    public long? VramBytes { get; init; }
    public string? DriverVersion { get; init; }
    /// <summary>True for a dedicated card (NVIDIA/AMD), false for an integrated GPU (iGPU).</summary>
    public bool IsDiscrete { get; init; }
}

public sealed class MemoryInfo
{
    public long TotalBytes { get; init; }
    public long? AvailableBytes { get; init; }
    /// <summary>Configured memory speed in MT/s (e.g. 3200). Null when unavailable.</summary>
    public int? SpeedMtPerSecond { get; init; }
    /// <summary>Number of populated memory sticks; 1 usually means single-channel.</summary>
    public int? ModuleCount { get; init; }
}

public sealed class DisplayInfo
{
    public string Name { get; init; } = "Unknown";
    public int WidthPixels { get; init; }
    public int HeightPixels { get; init; }
    /// <summary>Current refresh rate in Hz. Null when unavailable.</summary>
    public int? RefreshRateHz { get; init; }
    public bool IsPrimary { get; init; }
}

public enum StorageMediaType
{
    Unknown,
    Hdd,
    Ssd,
    /// <summary>Storage-class memory such as Intel Optane.</summary>
    Scm,
}

public sealed class StorageDeviceInfo
{
    public string Model { get; init; } = "Unknown";
    public StorageMediaType MediaType { get; init; }
    public long TotalBytes { get; init; }
}

/// <summary>A mounted volume (drive letter on Windows, mount point on Linux).</summary>
public sealed class VolumeInfo
{
    public string MountPoint { get; init; } = "Unknown";
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
}

public sealed class OperatingSystemInfo
{
    public string Name { get; init; } = "Unknown";
    public string Version { get; init; } = "Unknown";
    public string Architecture { get; init; } = "Unknown";
}
