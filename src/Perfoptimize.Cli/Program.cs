using Perfoptimize.Core.Formatting;
using Perfoptimize.Core.Games;
using Perfoptimize.Core.Hardware;
using Perfoptimize.Core.Monitoring;
using Perfoptimize.Core.Monitoring.Windows;
using Perfoptimize.Core.Optimization;

// Perfoptimize CLI — game optimization assistant.
//
// Usage:
//   perfoptimize [scan]            Print a human-readable hardware report.
//   perfoptimize scan --json [f]   Print (or save) the hardware profile as JSON.
//   perfoptimize games             List the known game profiles.
//   perfoptimize monitor [-d 30]   Detect the running game, sample telemetry,
//                                  identify the bottleneck and print advice.

var command = args.FirstOrDefault(a => !a.StartsWith('-')) ?? "scan";

try
{
    return command switch
    {
        "scan" => RunScan(args),
        "games" => RunGames(),
        "monitor" => await RunMonitorAsync(args),
        _ => Fail($"Unknown command \"{command}\". Available: scan, games, monitor."),
    };
}
catch (PlatformNotSupportedException ex)
{
    return Fail(ex.Message);
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static int RunScan(string[] args)
{
    var profile = new HardwareScanner().Scan();

    var flagIndex = Array.IndexOf(args, "--json");
    if (flagIndex >= 0)
    {
        var json = HardwareProfileJson.Serialize(profile);
        var jsonPath = flagIndex + 1 < args.Length ? args[flagIndex + 1] : null;
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

    PrintHardwareReport(profile);
    return 0;
}

static int RunGames()
{
    var profiles = GameProfileRepository.LoadBuiltIn();
    Console.WriteLine($"{profiles.Count} known game profile(s):");
    Console.WriteLine();
    foreach (var game in profiles)
    {
        Console.WriteLine($"- {game.Name}");
        Console.WriteLine($"    processes : {string.Join(", ", game.Executables)}");
        Console.WriteLine($"    settings  : {game.Settings.Count} referenced");
    }

    var running = new RunningGameDetector().Detect(profiles);
    Console.WriteLine();
    Console.WriteLine(running is null
        ? "No known game is currently running."
        : $"Currently running: {running.Profile.Name} (pid {running.ProcessId})");
    return 0;
}

static async Task<int> RunMonitorAsync(string[] args)
{
    var durationSeconds = 30;
    var durationIndex = Array.FindIndex(args, a => a is "-d" or "--duration");
    if (durationIndex >= 0 && durationIndex + 1 < args.Length &&
        int.TryParse(args[durationIndex + 1], out var parsed) && parsed > 0)
        durationSeconds = parsed;

    var hardware = new HardwareScanner().Scan();
    var games = GameProfileRepository.LoadBuiltIn();
    var running = new RunningGameDetector().Detect(games);

    Console.WriteLine(running is null
        ? "No known game detected — monitoring the whole system."
        : $"Detected game: {running.Profile.Name} (pid {running.ProcessId})");
    Console.WriteLine($"Sampling for {durationSeconds}s (1 sample/s)... play normally during the capture.");
    Console.WriteLine();

    ISystemTelemetrySource source = OperatingSystem.IsWindows()
        ? new WindowsTelemetrySource(hardware.PrimaryGpu?.VramBytes)
        : PerformanceMonitor.CreateForCurrentPlatform();
    var monitor = new PerformanceMonitor(source);

    var progress = new Progress<TelemetrySample>(s =>
        Console.WriteLine($"  cpu {Percent(s.CpuUsagePercent),6} | gpu {Percent(s.GpuUsagePercent),6} | " +
                          $"ram {Percent(s.RamUsagePercent),6} | vram {Percent(s.VramUsagePercent),6}"));
    var report = await monitor.CaptureAsync(TimeSpan.FromSeconds(durationSeconds), onSample: progress);

    var summary = report.Summary;
    Console.WriteLine();
    Console.WriteLine("=== Summary ===");
    Console.WriteLine($"Samples : {summary.SampleCount} over {summary.Duration.TotalSeconds:0}s");
    Console.WriteLine($"CPU     : avg {Percent(summary.AverageCpuUsagePercent)}, peak {Percent(summary.PeakCpuUsagePercent)}");
    Console.WriteLine($"GPU     : avg {Percent(summary.AverageGpuUsagePercent)}, peak {Percent(summary.PeakGpuUsagePercent)}");
    Console.WriteLine($"RAM     : avg {Percent(summary.AverageRamUsagePercent)}, peak {Percent(summary.PeakRamUsagePercent)}");
    Console.WriteLine($"VRAM    : avg {Percent(summary.AverageVramUsagePercent)}, peak {Percent(summary.PeakVramUsagePercent)}");
    if (summary.PeakCpuTemperatureC is { } cpuTemp)
        Console.WriteLine($"CPU temp: peak {cpuTemp:0}°C");
    if (summary.PeakGpuTemperatureC is { } gpuTemp)
        Console.WriteLine($"GPU temp: peak {gpuTemp:0}°C");

    var analysis = BottleneckDetector.Analyze(summary);
    Console.WriteLine();
    Console.WriteLine($"Bottleneck: {analysis.Type}");
    Console.WriteLine(analysis.Explanation);

    var recommendations = RecommendationEngine.Recommend(analysis, running?.Profile);
    if (recommendations.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Recommendations:");
        foreach (var recommendation in recommendations)
        {
            Console.WriteLine($"- {recommendation.Title}");
            Console.WriteLine($"    {recommendation.Reason}");
        }
    }
    return 0;
}

static string Percent(double? value) => value is { } v ? $"{v:0}%" : "n/a";

static void PrintHardwareReport(HardwareProfile profile)
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
