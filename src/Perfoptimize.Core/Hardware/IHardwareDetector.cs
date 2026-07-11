namespace Perfoptimize.Core.Hardware;

/// <summary>
/// Platform-specific hardware detection. One implementation per OS;
/// <see cref="HardwareScanner"/> picks the right one at runtime.
/// </summary>
public interface IHardwareDetector
{
    CpuInfo DetectCpu();
    IReadOnlyList<GpuInfo> DetectGpus();
    MemoryInfo DetectMemory();
    IReadOnlyList<DisplayInfo> DetectDisplays();
    IReadOnlyList<StorageDeviceInfo> DetectStorage();
    OperatingSystemInfo DetectOperatingSystem();
}
