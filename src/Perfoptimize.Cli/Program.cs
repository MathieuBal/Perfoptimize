using Perfoptimize.Core.Formatting;
using Perfoptimize.Core.Hardware;

// Perfoptimize CLI — step 1 of the game optimization assistant:
// detect the machine's CPU, GPU(s), RAM, VRAM, displays, storage and OS.
//
// Usage:
//   perfoptimize                 Print a human-readable hardware report.
//   perfoptimize --json          Print the profile as JSON on stdout.
//   perfoptimize --json <file>   Also save the JSON profile to a file.

var flagIndex = Array.IndexOf(args, "--json");
var jsonRequested = flagIndex >= 0;
var jsonPath = flagIndex >= 0 && flagIndex + 1 < args.Length ? args[flagIndex + 1] : null;

HardwareProfile profile;
try
{
    profile = new HardwareScanner().Scan();
}
catch (PlatformNotSupportedException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

if (jsonRequested)
{
    var json = HardwareProfileJson.Serialize(profile);
    if (jsonPath is not null)
    {
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"Hardware profile saved to {Path.GetFullPath(jsonPath)}");
    }
    else
    {
        Console.WriteLine(json);
    }
    return 0;
}

PrintReport(profile);
return 0;

static void PrintReport(HardwareProfile profile)
{
    Console.WriteLine("=== Perfoptimize — Hardware profile ===");
    Console.WriteLine();

    Console.WriteLine($"CPU     : {profile.Cpu.Name}");
    Console.WriteLine($"          {profile.Cpu.PhysicalCores} cores / {profile.Cpu.LogicalCores} threads" +
                      (profile.Cpu.MaxClockMhz is { } clock ? $", up to {clock / 1000.0:0.0} GHz" : ""));

    if (profile.Gpus.Count == 0)
    {
        Console.WriteLine("GPU     : not detected");
    }
    else
    {
        foreach (var gpu in profile.Gpus)
        {
            var kind = gpu.IsDiscrete ? "discrete" : "integrated";
            var vram = gpu.VramBytes is { } bytes ? $", {ByteSize.Humanize(bytes)} VRAM" : "";
            var driver = gpu.DriverVersion is { } version ? $", driver {version}" : "";
            Console.WriteLine($"GPU     : {gpu.Name} ({kind}{vram}{driver})");
        }
    }

    var ramDetails = new List<string>();
    if (profile.Memory.AvailableBytes is { } available)
        ramDetails.Add($"{ByteSize.Humanize(available)} available");
    if (profile.Memory.SpeedMtPerSecond is { } speed)
        ramDetails.Add($"{speed} MT/s");
    if (profile.Memory.ModuleCount is { } modules)
        ramDetails.Add($"{modules} module(s)");
    Console.WriteLine($"RAM     : {ByteSize.Humanize(profile.Memory.TotalBytes)}" +
                      (ramDetails.Count > 0 ? $" ({string.Join(", ", ramDetails)})" : ""));

    if (profile.Displays.Count == 0)
    {
        Console.WriteLine("Display : not detected");
    }
    else
    {
        foreach (var display in profile.Displays)
        {
            var refresh = display.RefreshRateHz is { } hz ? $" @ {hz} Hz" : "";
            var primary = display.IsPrimary ? " (primary)" : "";
            Console.WriteLine($"Display : {display.Name} — {display.WidthPixels}×{display.HeightPixels}{refresh}{primary}");
        }
    }

    foreach (var device in profile.StorageDevices)
    {
        var media = device.MediaType switch
        {
            StorageMediaType.Ssd => "SSD",
            StorageMediaType.Hdd => "HDD",
            StorageMediaType.Scm => "SCM",
            _ => "disk",
        };
        Console.WriteLine($"Storage : {device.Model} ({media}, {ByteSize.Humanize(device.TotalBytes)})");
    }

    foreach (var volume in profile.Volumes)
    {
        Console.WriteLine($"Volume  : {volume.MountPoint} — {ByteSize.Humanize(volume.FreeBytes)} free" +
                          $" of {ByteSize.Humanize(volume.TotalBytes)}");
    }

    Console.WriteLine($"OS      : {profile.OperatingSystem.Name} " +
                      $"({profile.OperatingSystem.Version}, {profile.OperatingSystem.Architecture})");

    if (profile.PrimaryGpu is { } primaryGpu)
    {
        Console.WriteLine();
        Console.WriteLine($"Gaming GPU: {primaryGpu.Name}");
    }
}
