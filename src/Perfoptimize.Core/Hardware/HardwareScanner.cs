using Perfoptimize.Core.Hardware.Linux;
using Perfoptimize.Core.Hardware.Windows;

namespace Perfoptimize.Core.Hardware;

/// <summary>
/// Entry point of the hardware detection module: runs every detector and
/// assembles the machine's <see cref="HardwareProfile"/>. Each component is
/// detected independently so one failing probe never aborts the whole scan.
/// </summary>
public sealed class HardwareScanner
{
    private readonly IHardwareDetector _detector;

    public HardwareScanner() : this(CreateForCurrentPlatform()) { }

    public HardwareScanner(IHardwareDetector detector) => _detector = detector;

    public static IHardwareDetector CreateForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsHardwareDetector();
        if (OperatingSystem.IsLinux())
            return new LinuxHardwareDetector();
        throw new PlatformNotSupportedException(
            $"Hardware detection is not supported on {Environment.OSVersion.Platform}.");
    }

    public HardwareProfile Scan()
    {
        return new HardwareProfile
        {
            Cpu = Probe(_detector.DetectCpu, new CpuInfo()),
            Gpus = Probe(_detector.DetectGpus, []),
            Memory = Probe(_detector.DetectMemory, new MemoryInfo()),
            Displays = Probe(_detector.DetectDisplays, []),
            StorageDevices = Probe(_detector.DetectStorage, []),
            Volumes = Probe(DetectVolumes, []),
            OperatingSystem = Probe(_detector.DetectOperatingSystem, new OperatingSystemInfo()),
        };
    }

    /// <summary>Volumes are enumerated with <see cref="DriveInfo"/>, which works on every platform.</summary>
    private static IReadOnlyList<VolumeInfo> DetectVolumes()
    {
        return DriveInfo.GetDrives()
            .Where(d => d is { IsReady: true, DriveType: DriveType.Fixed })
            .Select(d => new VolumeInfo
            {
                MountPoint = d.Name,
                TotalBytes = d.TotalSize,
                FreeBytes = d.AvailableFreeSpace,
            })
            .ToList();
    }

    private static T Probe<T>(Func<T> detect, T fallback)
    {
        try
        {
            return detect();
        }
        catch
        {
            // A single unreadable component (missing driver, restricted WMI, ...)
            // must not prevent the rest of the profile from being built.
            return fallback;
        }
    }
}
