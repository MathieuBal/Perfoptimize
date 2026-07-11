using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Perfoptimize.Core.Hardware.Windows;

/// <summary>
/// Windows implementation of hardware detection, based on WMI (no code is ever
/// injected into game processes) plus the registry for reliable VRAM sizes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHardwareDetector : IHardwareDetector
{
    private const string DisplayAdapterClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public CpuInfo DetectCpu()
    {
        var processors = Query(
            "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
        if (processors.Count == 0)
            return new CpuInfo { LogicalCores = Environment.ProcessorCount };

        var first = processors[0];
        return new CpuInfo
        {
            Name = (first["Name"] as string)?.Trim() ?? "Unknown",
            Vendor = (first["Manufacturer"] as string)?.Trim() ?? "Unknown",
            // Sum over sockets so multi-CPU machines are reported correctly.
            PhysicalCores = processors.Sum(p => ToInt(p["NumberOfCores"]) ?? 0),
            LogicalCores = processors.Sum(p => ToInt(p["NumberOfLogicalProcessors"]) ?? 0),
            MaxClockMhz = ToInt(first["MaxClockSpeed"]),
        };
    }

    public IReadOnlyList<GpuInfo> DetectGpus()
    {
        var vramByDescription = ReadVramFromRegistry();
        var gpus = new List<GpuInfo>();
        foreach (var controller in Query(
            "SELECT Name, AdapterCompatibility, AdapterRAM, DriverVersion FROM Win32_VideoController"))
        {
            var name = (controller["Name"] as string)?.Trim();
            if (string.IsNullOrEmpty(name))
                continue;

            var vendor = GpuClassifier.NormalizeVendor(controller["AdapterCompatibility"] as string, name);

            // AdapterRAM is a 32-bit value and caps at 4 GiB; the registry's
            // qwMemorySize (64-bit) is authoritative when present.
            long? vram = vramByDescription.TryGetValue(name, out var qwSize) ? qwSize : null;
            if (vram is null)
            {
                var adapterRam = ToLong(controller["AdapterRAM"]);
                if (adapterRam > 0)
                    vram = adapterRam;
            }

            gpus.Add(new GpuInfo
            {
                Name = name,
                Vendor = vendor,
                VramBytes = vram,
                DriverVersion = controller["DriverVersion"] as string,
                IsDiscrete = GpuClassifier.IsDiscrete(name, vendor),
            });
        }
        return gpus;
    }

    public MemoryInfo DetectMemory()
    {
        var system = Query("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault();
        var os = Query("SELECT FreePhysicalMemory FROM Win32_OperatingSystem").FirstOrDefault();
        var modules = Query("SELECT ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");

        var speeds = modules
            .Select(m => ToInt(m["ConfiguredClockSpeed"]) ?? ToInt(m["Speed"]))
            .Where(s => s is > 0)
            .ToList();

        return new MemoryInfo
        {
            TotalBytes = system is null ? 0 : ToLong(system["TotalPhysicalMemory"]) ?? 0,
            // FreePhysicalMemory is reported in kilobytes.
            AvailableBytes = os is null ? null : ToLong(os["FreePhysicalMemory"]) * 1024,
            SpeedMtPerSecond = speeds.Count > 0 ? speeds.Max() : null,
            ModuleCount = modules.Count > 0 ? modules.Count : null,
        };
    }

    public IReadOnlyList<DisplayInfo> DetectDisplays()
    {
        var monitorNames = ReadMonitorNames();
        var displays = new List<DisplayInfo>();
        foreach (var controller in Query(
            "SELECT CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController"))
        {
            var width = ToInt(controller["CurrentHorizontalResolution"]);
            var height = ToInt(controller["CurrentVerticalResolution"]);
            if (width is not > 0 || height is not > 0)
                continue; // Adapter without an active output.

            var refresh = ToInt(controller["CurrentRefreshRate"]);
            displays.Add(new DisplayInfo
            {
                Name = displays.Count < monitorNames.Count ? monitorNames[displays.Count] : "Display",
                WidthPixels = width.Value,
                HeightPixels = height.Value,
                RefreshRateHz = refresh is > 1 ? refresh : null,
                IsPrimary = displays.Count == 0,
            });
        }
        return displays;
    }

    public IReadOnlyList<StorageDeviceInfo> DetectStorage()
    {
        // MSFT_PhysicalDisk knows whether a disk is an SSD; Win32_DiskDrive does not.
        var msftDisks = Query(
            "SELECT FriendlyName, MediaType, Size FROM MSFT_PhysicalDisk",
            @"root\Microsoft\Windows\Storage");
        if (msftDisks.Count > 0)
        {
            return msftDisks.Select(d => new StorageDeviceInfo
            {
                Model = (d["FriendlyName"] as string)?.Trim() ?? "Unknown",
                MediaType = WindowsParsing.MapMsftMediaType(ToInt(d["MediaType"]) ?? 0),
                TotalBytes = ToLong(d["Size"]) ?? 0,
            }).ToList();
        }

        return Query("SELECT Model, Size FROM Win32_DiskDrive")
            .Select(d => new StorageDeviceInfo
            {
                Model = (d["Model"] as string)?.Trim() ?? "Unknown",
                MediaType = StorageMediaType.Unknown,
                TotalBytes = ToLong(d["Size"]) ?? 0,
            }).ToList();
    }

    public OperatingSystemInfo DetectOperatingSystem()
    {
        var os = Query("SELECT Caption, Version FROM Win32_OperatingSystem").FirstOrDefault();
        return new OperatingSystemInfo
        {
            Name = (os?["Caption"] as string)?.Trim() ?? "Windows",
            Version = (os?["Version"] as string)?.Trim() ?? Environment.OSVersion.Version.ToString(),
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
        };
    }

    /// <summary>
    /// Reads "HardwareInformation.qwMemorySize" for every display adapter in the
    /// registry, keyed by the adapter description (matches Win32_VideoController.Name).
    /// </summary>
    private static Dictionary<string, long> ReadVramFromRegistry()
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(DisplayAdapterClassKey);
            if (classKey is null)
                return result;

            foreach (var subKeyName in classKey.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
            {
                using var adapterKey = classKey.OpenSubKey(subKeyName);
                if (adapterKey?.GetValue("DriverDesc") is not string description)
                    continue;
                if (adapterKey.GetValue("HardwareInformation.qwMemorySize") is not long size || size <= 0)
                    continue;
                // Keep the largest value if the same GPU model appears twice.
                if (!result.TryGetValue(description, out var existing) || size > existing)
                    result[description] = size;
            }
        }
        catch
        {
            // Registry access can be restricted; VRAM will fall back to AdapterRAM.
        }
        return result;
    }

    private static List<string> ReadMonitorNames()
    {
        var names = new List<string>();
        foreach (var monitor in Query("SELECT UserFriendlyName FROM WmiMonitorID", @"root\wmi"))
        {
            var name = WindowsParsing.DecodeMonitorName(monitor["UserFriendlyName"] as ushort[]);
            names.Add(name ?? "Display");
        }
        return names;
    }

    private static List<Dictionary<string, object?>> Query(string wql, string scope = @"root\cimv2") =>
        WmiQuery.Run(wql, scope);

    private static int? ToInt(object? value) => WmiQuery.ToInt(value);

    private static long? ToLong(object? value) => WmiQuery.ToLong(value);
}
