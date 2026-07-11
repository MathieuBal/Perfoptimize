using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Perfoptimize.Core.Hardware.Linux;

/// <summary>
/// Linux implementation of hardware detection, based on /proc, /sys and a few
/// standard tools (lspci, xrandr, nvidia-smi) when they are available.
/// Useful for development machines and Proton/Steam Deck style setups.
/// </summary>
public sealed class LinuxHardwareDetector : IHardwareDetector
{
    public CpuInfo DetectCpu()
    {
        var parsed = LinuxProcParser.ParseCpuInfo(ReadFileOrEmpty("/proc/cpuinfo"));
        return new CpuInfo
        {
            Name = parsed.Name,
            Vendor = parsed.Vendor,
            PhysicalCores = parsed.PhysicalCores,
            LogicalCores = parsed.LogicalCores > 0 ? parsed.LogicalCores : Environment.ProcessorCount,
            MaxClockMhz = ReadMaxClockMhz(),
        };
    }

    public IReadOnlyList<GpuInfo> DetectGpus()
    {
        var gpus = LinuxProcParser.ParseLspci(RunOrEmpty("lspci", "-mm"));
        if (gpus.Count == 0)
            return gpus;

        var nvidiaVram = ReadNvidiaVramBytes();
        var amdVram = ReadAmdVramBytes();
        return gpus.Select(gpu => new GpuInfo
        {
            Name = gpu.Name,
            Vendor = gpu.Vendor,
            IsDiscrete = gpu.IsDiscrete,
            VramBytes = gpu.Vendor switch
            {
                "NVIDIA" => nvidiaVram,
                "AMD" => amdVram,
                _ => null,
            },
        }).ToList();
    }

    public MemoryInfo DetectMemory() =>
        LinuxProcParser.ParseMemInfo(ReadFileOrEmpty("/proc/meminfo"));

    public IReadOnlyList<DisplayInfo> DetectDisplays()
    {
        // Headless sessions (CI, servers) have no display server to query.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            return [];
        return LinuxProcParser.ParseXrandr(RunOrEmpty("xrandr", "--query"));
    }

    public IReadOnlyList<StorageDeviceInfo> DetectStorage()
    {
        var devices = new List<StorageDeviceInfo>();
        if (!Directory.Exists("/sys/block"))
            return devices;

        foreach (var blockPath in Directory.GetDirectories("/sys/block").OrderBy(p => p))
        {
            var deviceName = Path.GetFileName(blockPath);
            // Skip virtual devices (loopbacks, device-mapper, ramdisks, zram).
            if (deviceName.StartsWith("loop") || deviceName.StartsWith("dm-") ||
                deviceName.StartsWith("ram") || deviceName.StartsWith("zram") ||
                deviceName.StartsWith("sr"))
                continue;

            var sectors = long.TryParse(ReadFileOrEmpty($"{blockPath}/size").Trim(), out var s) ? s : 0;
            if (sectors <= 0)
                continue;

            var rotationalText = ReadFileOrEmpty($"{blockPath}/queue/rotational").Trim();
            var mediaType = deviceName.StartsWith("nvme") || rotationalText == "0"
                ? StorageMediaType.Ssd
                : rotationalText == "1" ? StorageMediaType.Hdd : StorageMediaType.Unknown;

            var model = ReadFileOrEmpty($"{blockPath}/device/model").Trim();
            devices.Add(new StorageDeviceInfo
            {
                Model = model.Length > 0 ? model : deviceName,
                MediaType = mediaType,
                TotalBytes = sectors * 512, // /sys/block sizes are in 512-byte sectors.
            });
        }
        return devices;
    }

    public OperatingSystemInfo DetectOperatingSystem() =>
        LinuxProcParser.ParseOsRelease(
            ReadFileOrEmpty("/etc/os-release"),
            RuntimeInformation.OSArchitecture.ToString());

    private static int? ReadMaxClockMhz()
    {
        // cpuinfo_max_freq is expressed in kHz.
        var text = ReadFileOrEmpty("/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq").Trim();
        return long.TryParse(text, out var khz) && khz > 0 ? (int)(khz / 1000) : null;
    }

    private static long? ReadNvidiaVramBytes()
    {
        var output = RunOrEmpty("nvidia-smi", "--query-gpu=memory.total --format=csv,noheader,nounits");
        var firstLine = output.Split('\n').FirstOrDefault()?.Trim();
        return long.TryParse(firstLine, out var mib) && mib > 0 ? mib * 1024L * 1024L : null;
    }

    private static long? ReadAmdVramBytes()
    {
        if (!Directory.Exists("/sys/class/drm"))
            return null;
        foreach (var card in Directory.GetDirectories("/sys/class/drm", "card?"))
        {
            var text = ReadFileOrEmpty($"{card}/device/mem_info_vram_total").Trim();
            if (long.TryParse(text, out var bytes) && bytes > 0)
                return bytes;
        }
        return null;
    }

    private static string ReadFileOrEmpty(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string RunOrEmpty(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
                return string.Empty;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output;
        }
        catch
        {
            return string.Empty; // Tool not installed.
        }
    }
}
