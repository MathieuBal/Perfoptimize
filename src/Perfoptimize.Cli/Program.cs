using Perfoptimize.Core.Configs;
using Perfoptimize.Core.Formatting;
using Perfoptimize.Core.Games;
using Perfoptimize.Core.Hardware;
using Perfoptimize.Core.Monitoring;
using Perfoptimize.Core.Monitoring.Frametimes;
using Perfoptimize.Core.Monitoring.Windows;
using Perfoptimize.Core.Optimization;
using Perfoptimize.Core.Storage;

// Perfoptimize CLI — game optimization assistant.
//
// Usage:
//   perfoptimize [scan]              Print a human-readable hardware report.
//   perfoptimize scan --json [f]     Print (or save) the hardware profile as JSON.
//   perfoptimize games               List the known game profiles.
//   perfoptimize monitor [-d 30] [--goal quality|balanced|performance]
//                        [--target-fps 60] [--frametimes capture.csv]
//                                    Detect the running game, sample telemetry,
//                                    identify the bottleneck and print advice
//                                    sized to the FPS gap when a PresentMon
//                                    capture of the session is provided.
//   perfoptimize frametimes <csv>    Analyze a PresentMon capture (FPS, 1% low, stutter).
//   perfoptimize history [game]      Show past monitoring sessions.
//   perfoptimize config <game>       Locate a game's config files.
//     --backup                       Back them up (timestamped copies).
//     --restore                      Restore the most recent backup (game must be closed).
//   perfoptimize apply <game>        Preview the config edits implementing the
//                                    recommendations (dry run by default).
//     --session <id>                 Apply exactly the plan stored by that monitor session.
//     --bottleneck gpu|cpu|...       Bottleneck to optimize for (default: last session's plan).
//     --goal quality|balanced|performance
//     --write                        Actually write the files (backup first, game closed).
//   perfoptimize tune <game>         Measured loop: baseline FPS -> ONE change ->
//                                    re-measure -> keep it only if the numbers improved.
//     --baseline/--after <csv>       Offline mode with two PresentMon captures.

var command = args.FirstOrDefault(a => !a.StartsWith('-')) ?? "scan";

try
{
    return command switch
    {
        "scan" => RunScan(args),
        "games" => RunGames(),
        "monitor" => await RunMonitorAsync(args),
        "frametimes" => RunFrametimes(args),
        "history" => RunHistory(args),
        "config" => RunConfig(args),
        "apply" => RunApply(args),
        "tune" => await RunTuneAsync(args),
        _ => Fail($"Unknown command \"{command}\". Available: scan, games, monitor, frametimes, history, config, apply, tune."),
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

    var goal = OptimizationGoal.Balanced;
    var goalIndex = Array.IndexOf(args, "--goal");
    if (goalIndex >= 0 && goalIndex + 1 < args.Length &&
        !Enum.TryParse(args[goalIndex + 1], ignoreCase: true, out goal))
        return Fail($"Unknown goal \"{args[goalIndex + 1]}\". Available: quality, balanced, performance.");

    int? targetFps = null;
    var targetIndex = Array.IndexOf(args, "--target-fps");
    if (targetIndex >= 0)
    {
        if (targetIndex + 1 >= args.Length ||
            !int.TryParse(args[targetIndex + 1], out var parsedTarget) || parsedTarget <= 0)
            return Fail("Usage: --target-fps <positive number>");
        targetFps = parsedTarget;
    }

    FrametimeStatistics? frametimeStats = null;
    var frametimesIndex = Array.IndexOf(args, "--frametimes");
    if (frametimesIndex >= 0)
    {
        if (frametimesIndex + 1 >= args.Length || !File.Exists(args[frametimesIndex + 1]))
            return Fail("Usage: --frametimes <presentmon.csv> (file must exist)");
        frametimeStats = FrametimeAnalyzer.Analyze(
            PresentMonCsv.ReadFrametimes(File.ReadAllText(args[frametimesIndex + 1])));
        if (frametimeStats is null)
            return Fail("No frame times found in the provided capture.");
    }

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

    // On Windows, capture real frame times with PresentMon in parallel with the
    // telemetry session — unless the user already provided a capture file.
    Task<FrametimeStatistics?>? presentMonTask = null;
    if (frametimeStats is null && running is not null && OperatingSystem.IsWindows())
    {
        if (PresentMonCapture.FindExecutable() is not null)
        {
            presentMonTask = PresentMonCapture.CaptureAsync(
                running.ProcessId, TimeSpan.FromSeconds(durationSeconds));
            Console.WriteLine("PresentMon found — capturing real frame times alongside telemetry.");
        }
        else
        {
            Console.WriteLine("Tip: install Intel PresentMon (on PATH or PERFOPTIMIZE_PRESENTMON) " +
                              "to capture real FPS during monitoring.");
        }
    }

    var progress = new Progress<TelemetrySample>(s =>
        Console.WriteLine($"  cpu {Percent(s.CpuUsagePercent),6} | core max {Percent(s.MaxCoreUsagePercent),6} | " +
                          $"gpu {Percent(s.GpuUsagePercent),6} | ram {Percent(s.RamUsagePercent),6} | " +
                          $"vram {Percent(s.VramUsagePercent),6}"));
    var report = await monitor.CaptureAsync(
        TimeSpan.FromSeconds(durationSeconds), onSample: progress, gameProcessId: running?.ProcessId);

    if (presentMonTask is not null)
        frametimeStats = await presentMonTask;

    var summary = report.Summary;
    Console.WriteLine();
    Console.WriteLine("=== Summary ===");
    Console.WriteLine($"Samples : {summary.SampleCount} over {summary.Duration.TotalSeconds:0}s");
    Console.WriteLine($"CPU     : avg {Percent(summary.AverageCpuUsagePercent)}, peak {Percent(summary.PeakCpuUsagePercent)}, " +
                      $"busiest core avg {Percent(summary.AverageMaxCoreUsagePercent)}");
    if (summary.AverageGameCpuUsagePercent is { } gameCpu)
        Console.WriteLine($"          game process: {gameCpu:0}% of the machine");
    Console.WriteLine($"GPU     : avg {Percent(summary.AverageGpuUsagePercent)}, peak {Percent(summary.PeakGpuUsagePercent)}" +
                      (summary.AverageGameGpuUsagePercent is { } gameGpu ? $", game share {gameGpu:0}%" : ""));
    Console.WriteLine($"RAM     : avg {Percent(summary.AverageRamUsagePercent)}, peak {Percent(summary.PeakRamUsagePercent)}");
    Console.WriteLine($"VRAM    : avg {Percent(summary.AverageVramUsagePercent)}, peak {Percent(summary.PeakVramUsagePercent)}");
    if (summary.PeakCpuTemperatureC is { } cpuTemp)
        Console.WriteLine($"CPU temp: peak {cpuTemp:0}°C");
    if (summary.PeakGpuTemperatureC is { } gpuTemp)
        Console.WriteLine($"GPU temp: peak {gpuTemp:0}°C" +
                          (summary.GpuThermalThrottleObserved ? " — driver reported thermal slowdown" : ""));
    if (summary.AverageGpuCoreClockMhz is { } clock)
        Console.WriteLine($"GPU clk : avg {clock:0} MHz");

    var analysis = BottleneckDetector.Analyze(summary, hardware.Cpu.LogicalCores > 0
        ? hardware.Cpu.LogicalCores
        : Environment.ProcessorCount);
    Console.WriteLine();
    var confidence = analysis.Confidence == BottleneckConfidence.Suspected ? " (suspected)" : "";
    Console.WriteLine($"Bottleneck: {analysis.Type}{confidence}");
    Console.WriteLine(analysis.Explanation);

    var plan = RecommendationEngine.Advise(new AdviceRequest
    {
        Analysis = analysis,
        Game = running?.Profile,
        Goal = goal,
        Hardware = hardware,
        Summary = summary,
        Frametimes = frametimeStats,
        TargetFps = targetFps,
    });

    Console.WriteLine();
    if (plan.MeasuredAverageFps is { } fps)
    {
        var gap = plan.RequiredGainPercent is { } required and > 0
            ? $" — +{required:0}% needed"
            : " — target met";
        Console.WriteLine($"FPS: {fps:0.#} measured, {plan.TargetFps} targeted{gap}");
        if (plan is { EstimatedGainLowPercent: { } low, EstimatedGainHighPercent: { } high })
            Console.WriteLine(Math.Abs(high - low) < 0.5
                ? $"Estimated gain of this plan: ~+{high:0}%"
                : $"Estimated gain of this plan: +{low:0}% to +{high:0}% (rough)");
    }
    else
    {
        Console.WriteLine($"Target: {plan.TargetFps} FPS (add --frametimes <capture.csv> to size " +
                          "the plan to your real FPS gap)");
    }

    if (plan.Recommendations.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"Recommendations ({goal} goal):");
        foreach (var recommendation in plan.Recommendations)
        {
            var gain = recommendation.ExpectedGainPercent is { } g ? $" (~+{g:0}%)" : "";
            Console.WriteLine($"- {recommendation.Title}{gain}");
            Console.WriteLine($"    {recommendation.Reason}");
        }
    }

    foreach (var note in plan.Notes)
        Console.WriteLine($"Note: {note}");

    try
    {
        using var store = new SessionStore(SessionStore.DefaultDatabasePath());
        var id = store.Save(new SessionRecord
        {
            StartedAt = DateTimeOffset.Now.AddSeconds(-durationSeconds),
            GameName = running?.Profile.Name,
            Bottleneck = analysis.Type,
            Summary = summary,
            TargetFps = plan.TargetFps,
            AverageFps = frametimeStats?.AverageFps,
            OnePercentLowFps = frametimeStats?.OnePercentLowFps,
            // The exact plan shown above, so 'apply' executes what was advised.
            Plan = plan.Recommendations.Select(r => new StoredRecommendation
            {
                Title = r.Title,
                Reason = r.Reason,
                SettingName = r.Setting?.Name,
                ExpectedGainPercent = r.ExpectedGainPercent,
            }).ToList(),
        });
        Console.WriteLine();
        Console.WriteLine($"Session #{id} saved — \"perfoptimize apply {running?.Profile.Name ?? "<game>"} " +
                          $"--session {id}\" applies exactly this plan.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Could not save the session: {ex.Message}");
    }
    return 0;
}

static int RunFrametimes(string[] args)
{
    string? csvPath = null;
    var tail = args.SkipWhile(a => a != "frametimes").Skip(1).ToArray();
    for (var i = 0; i < tail.Length; i++)
    {
        if (tail[i].StartsWith('-'))
        {
            i++; // skip the flag's value ("--process game.exe")
            continue;
        }
        csvPath = tail[i];
        break;
    }
    if (csvPath is null)
        return Fail("Usage: perfoptimize frametimes <presentmon.csv> [--process game.exe]");
    if (!File.Exists(csvPath))
        return Fail($"File not found: {csvPath}");

    string? processName = null;
    var processIndex = Array.IndexOf(args, "--process");
    if (processIndex >= 0 && processIndex + 1 < args.Length)
        processName = args[processIndex + 1];

    var frametimes = PresentMonCsv.ReadFrametimes(File.ReadAllText(csvPath), processName);
    var statistics = FrametimeAnalyzer.Analyze(frametimes);
    if (statistics is null)
        return Fail("No frame times found in this file. Is it a PresentMon capture" +
                    (processName is null ? "?" : $" containing \"{processName}\"?"));

    Console.WriteLine($"=== Frametime analysis — {Path.GetFileName(csvPath)} ===");
    Console.WriteLine();
    Console.WriteLine($"Frames analyzed : {statistics.FrameCount}");
    Console.WriteLine($"Average FPS     : {statistics.AverageFps:0.#}");
    Console.WriteLine($"1% low FPS      : {statistics.OnePercentLowFps:0.#}");
    Console.WriteLine($"Avg frametime   : {statistics.AverageFrametimeMs:0.##} ms");
    Console.WriteLine($"Worst frametime : {statistics.WorstFrametimeMs:0.##} ms");
    Console.WriteLine($"Stutters        : {statistics.StutterCount} ({statistics.StutterPercent:0.##}% of frames)");
    Console.WriteLine();
    Console.WriteLine(statistics.StutterPercent > 1
        ? "Frame delivery is uneven — prioritize fixing stutter (VRAM/RAM pressure, background apps) before chasing average FPS."
        : "Frame delivery is smooth.");
    return 0;
}

static int RunHistory(string[] args)
{
    var gameName = args.SkipWhile(a => a != "history").Skip(1)
        .FirstOrDefault(a => !a.StartsWith('-'));

    using var store = new SessionStore(SessionStore.DefaultDatabasePath());
    var sessions = store.GetRecent(limit: 10, gameName);
    if (sessions.Count == 0)
    {
        Console.WriteLine(gameName is null
            ? "No monitoring session recorded yet — run \"perfoptimize monitor\" first."
            : $"No session recorded for \"{gameName}\".");
        return 0;
    }

    Console.WriteLine($"Last {sessions.Count} session(s){(gameName is null ? "" : $" for {gameName}")}:");
    Console.WriteLine();
    foreach (var session in sessions)
    {
        var game = session.GameName ?? "system-wide";
        Console.WriteLine($"#{session.Id} — {session.StartedAt:yyyy-MM-dd HH:mm} — {game}");
        Console.WriteLine($"    bottleneck {session.Bottleneck}, " +
                          $"cpu avg {Percent(session.Summary.AverageCpuUsagePercent)}, " +
                          $"gpu avg {Percent(session.Summary.AverageGpuUsagePercent)}, " +
                          $"ram peak {Percent(session.Summary.PeakRamUsagePercent)}, " +
                          $"vram peak {Percent(session.Summary.PeakVramUsagePercent)}");
    }
    return 0;
}

static int RunConfig(string[] args)
{
    var query = args.SkipWhile(a => a != "config").Skip(1)
        .FirstOrDefault(a => !a.StartsWith('-'));
    if (query is null)
        return Fail("Usage: perfoptimize config <game> [--backup|--restore]");

    var profiles = GameProfileRepository.LoadBuiltIn();
    var game = profiles.FirstOrDefault(p =>
                   p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) ??
               RunningGameDetector.Match(query, profiles);
    if (game is null)
        return Fail($"No known game matches \"{query}\". See \"perfoptimize games\".");
    if (game.ConfigFiles.Count == 0)
        return Fail($"{game.Name} has no documented config file (its settings may live in the cloud or the registry).");

    var doBackup = args.Contains("--backup");
    var doRestore = args.Contains("--restore");
    if (doBackup && doRestore)
        return Fail("Choose either --backup or --restore, not both.");

    // Config files must never be written while the game can overwrite them on exit.
    if (doRestore && new RunningGameDetector().Detect([game]) is { } running)
        return Fail($"{game.Name} is running (pid {running.ProcessId}) — close it before restoring its config.");

    var backups = new ConfigBackupService(ConfigBackupService.DefaultBackupRoot());
    Console.WriteLine($"Config files for {game.Name}:");
    Console.WriteLine();

    var failures = 0;
    foreach (var configFile in game.ConfigFiles)
    {
        var path = configFile.ResolvePath();
        var exists = File.Exists(path);
        Console.WriteLine($"- {path}");
        Console.WriteLine($"    format {configFile.Format}, {(exists ? "found" : "not found")}, " +
                          $"{backups.ListBackups(game.Name, path).Count} backup(s)");

        if (doBackup)
        {
            if (!exists)
            {
                Console.WriteLine("    skipped backup: file not found");
                continue;
            }
            var backupPath = backups.Backup(game.Name, path);
            Console.WriteLine($"    backed up to {backupPath}");
        }
        else if (doRestore)
        {
            var restored = backups.RestoreLatest(game.Name, path);
            if (restored is null)
            {
                Console.WriteLine("    no backup to restore");
                failures++;
            }
            else
            {
                Console.WriteLine($"    restored from {Path.GetFileName(restored)}");
            }
        }
    }
    return failures == 0 ? 0 : 1;
}

static int RunApply(string[] args)
{
    var query = args.SkipWhile(a => a != "apply").Skip(1)
        .FirstOrDefault(a => !a.StartsWith('-'));
    if (query is null)
        return Fail("Usage: perfoptimize apply <game> [--session <id>] " +
                    "[--bottleneck gpu|cpu|vram|ram|thermal] " +
                    "[--goal quality|balanced|performance] [--write]");

    var profiles = GameProfileRepository.LoadBuiltIn();
    var game = profiles.FirstOrDefault(p =>
                   p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) ??
               RunningGameDetector.Match(query, profiles);
    if (game is null)
        return Fail($"No known game matches \"{query}\". See \"perfoptimize games\".");

    var goal = OptimizationGoal.Balanced;
    var goalIndex = Array.IndexOf(args, "--goal");
    if (goalIndex >= 0 && goalIndex + 1 < args.Length &&
        !Enum.TryParse(args[goalIndex + 1], ignoreCase: true, out goal))
        return Fail($"Unknown goal \"{args[goalIndex + 1]}\". Available: quality, balanced, performance.");

    // Preferred path: apply exactly the plan a monitoring session produced —
    // same settings the user saw, not a fresh computation from partial context.
    List<GameSetting>? settingsToLower = null;
    string planOrigin;
    var sessionIndex = Array.IndexOf(args, "--session");
    var bottleneckIndex = Array.IndexOf(args, "--bottleneck");
    BottleneckType bottleneck = BottleneckType.Unknown;

    if (sessionIndex >= 0)
    {
        if (sessionIndex + 1 >= args.Length || !long.TryParse(args[sessionIndex + 1], out var sessionId))
            return Fail("Usage: --session <id> (see \"perfoptimize history\")");
        using var store = new SessionStore(SessionStore.DefaultDatabasePath());
        var session = store.GetById(sessionId);
        if (session is null)
            return Fail($"Session #{sessionId} does not exist.");
        if (session.Plan.Count == 0)
            return Fail($"Session #{sessionId} has no stored plan (it predates plan storage or " +
                        "produced no recommendations).");
        settingsToLower = MatchPlanSettings(game, session.Plan);
        bottleneck = session.Bottleneck;
        planOrigin = $"plan of session #{sessionId}";
        if (settingsToLower.Count == 0)
            return Fail($"No setting of session #{sessionId}'s plan is bound to a config file " +
                        "— those changes must be made in the game's menu.");
    }
    else if (bottleneckIndex >= 0)
    {
        if (bottleneckIndex + 1 >= args.Length ||
            !Enum.TryParse(args[bottleneckIndex + 1], ignoreCase: true, out bottleneck))
            return Fail("Usage: --bottleneck gpu|cpu|vram|ram|thermal");
        planOrigin = $"{bottleneck} bottleneck (explicit)";
    }
    else
    {
        using var store = new SessionStore(SessionStore.DefaultDatabasePath());
        var lastSession = store.GetRecent(limit: 1, game.Name).FirstOrDefault();
        if (lastSession is null)
            return Fail($"No recorded session for {game.Name}. Run \"perfoptimize monitor\" while " +
                        "playing first, or pass --bottleneck explicitly.");
        bottleneck = lastSession.Bottleneck;
        if (lastSession.Plan.Count > 0)
        {
            settingsToLower = MatchPlanSettings(game, lastSession.Plan);
            planOrigin = $"plan of session #{lastSession.Id} ({lastSession.StartedAt:yyyy-MM-dd HH:mm})";
        }
        else
        {
            planOrigin = $"bottleneck of session #{lastSession.Id}: {bottleneck}";
        }
        Console.WriteLine($"Using the {planOrigin}.");
    }

    if (settingsToLower is null or { Count: 0 })
    {
        var recommendations = RecommendationEngine.Recommend(
            new BottleneckAnalysis { Type = bottleneck }, game, goal);
        settingsToLower = recommendations
            .Where(r => r.Setting is not null)
            .Select(r => r.Setting!)
            .ToList();
    }
    if (settingsToLower.Count == 0)
        return Fail($"No {game.Name} setting targets a {bottleneck} bottleneck.");

    var write = args.Contains("--write");
    if (write && new RunningGameDetector().Detect([game]) is { } running)
        return Fail($"{game.Name} is running (pid {running.ProcessId}) — close it before writing its config.");

    var applier = new SettingsApplier(new ConfigBackupService(ConfigBackupService.DefaultBackupRoot()));
    var result = applier.Apply(game, settingsToLower, write);

    Console.WriteLine();
    Console.WriteLine(result.Written
        ? $"Applied config changes for {game.Name} ({planOrigin}):"
        : $"Planned config changes for {game.Name} ({planOrigin}) — dry run:");
    foreach (var change in result.Changes)
    {
        var marker = change.Status == PlannedChangeStatus.Ready ? (result.Written ? "APPLIED" : "READY") : change.Status.ToString();
        Console.WriteLine($"- {change.Setting.Name} [{marker}]");
        Console.WriteLine($"    {change.Note}");
    }

    if (result.Written)
    {
        foreach (var backup in result.BackupPaths)
            Console.WriteLine($"Backup created: {backup}");
        Console.WriteLine("Restore anytime with \"perfoptimize config <game> --restore\".");
    }
    else if (result.Changes.Any(c => c.Status == PlannedChangeStatus.Ready))
    {
        Console.WriteLine();
        Console.WriteLine("Nothing was written. Re-run with --write to apply (a backup is taken first).");
    }
    return 0;
}

/// <summary>Maps a stored session plan back to the profile's setting objects.</summary>
static List<GameSetting> MatchPlanSettings(GameProfile game, IReadOnlyList<StoredRecommendation> plan)
{
    return plan
        .Where(r => r.SettingName is not null)
        .Select(r => game.Settings.FirstOrDefault(s =>
            s.Name.Equals(r.SettingName, StringComparison.OrdinalIgnoreCase)))
        .Where(s => s is not null)
        .Select(s => s!)
        .ToList();
}

static async Task<int> RunTuneAsync(string[] args)
{
    var query = args.SkipWhile(a => a != "tune").Skip(1)
        .FirstOrDefault(a => !a.StartsWith('-'));
    if (query is null)
        return Fail("Usage: perfoptimize tune <game> [--bottleneck gpu|...] [--goal ...]\n" +
                    "  Measured loop: baseline FPS -> ONE change -> re-measure -> keep or restore.\n" +
                    "  FPS source: PresentMon (auto, Windows) or --baseline/--after <presentmon.csv>.");

    var profiles = GameProfileRepository.LoadBuiltIn();
    var game = profiles.FirstOrDefault(p =>
                   p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) ??
               RunningGameDetector.Match(query, profiles);
    if (game is null)
        return Fail($"No known game matches \"{query}\". See \"perfoptimize games\".");

    var goal = OptimizationGoal.Balanced;
    var goalIndex = Array.IndexOf(args, "--goal");
    if (goalIndex >= 0 && goalIndex + 1 < args.Length &&
        !Enum.TryParse(args[goalIndex + 1], ignoreCase: true, out goal))
        return Fail($"Unknown goal \"{args[goalIndex + 1]}\". Available: quality, balanced, performance.");

    // Offline mode (any platform): the user provides two PresentMon captures.
    // Live mode (Windows + PresentMon): captures are taken automatically.
    var baselineCsv = ArgValue(args, "--baseline");
    var afterCsv = ArgValue(args, "--after");
    var offline = baselineCsv is not null;
    if (offline && afterCsv is null)
        return Fail("Offline tuning needs both --baseline <csv> and --after <csv>.");
    if (!offline && (!OperatingSystem.IsWindows() || PresentMonCapture.FindExecutable() is null))
        return Fail("Live tuning needs PresentMon (Windows). Alternatively, capture two PresentMon " +
                    "CSVs yourself and pass --baseline and --after.");

    // 1. Baseline measurement.
    FrametimeStatistics? baseline;
    if (offline)
    {
        baseline = FrametimeAnalyzer.Analyze(PresentMonCsv.ReadFrametimes(File.ReadAllText(baselineCsv!)));
    }
    else
    {
        var running = new RunningGameDetector().Detect([game]);
        if (running is null)
            return Fail($"{game.Name} is not running — start it and stay in a repeatable scene.");
        Console.WriteLine("Measuring baseline for 30s — keep playing the same scene...");
        baseline = await PresentMonCapture.CaptureAsync(running.ProcessId, TimeSpan.FromSeconds(30));
    }
    if (baseline is null)
        return Fail("Baseline measurement failed (no frames captured).");
    Console.WriteLine($"Baseline: {baseline.AverageFps:0.#} FPS average, " +
                      $"{baseline.OnePercentLowFps:0.#} FPS 1% low.");

    // 2. Choose ONE change: the advisor's first setting that is bound to a config file.
    var bottleneck = BottleneckType.Gpu;
    var bottleneckIndex = Array.IndexOf(args, "--bottleneck");
    if (bottleneckIndex >= 0 && bottleneckIndex + 1 < args.Length &&
        !Enum.TryParse(args[bottleneckIndex + 1], ignoreCase: true, out bottleneck))
        return Fail("Usage: --bottleneck gpu|cpu|vram|ram|thermal");

    var plan = RecommendationEngine.Advise(new AdviceRequest
    {
        Analysis = new BottleneckAnalysis { Type = bottleneck },
        Game = game,
        Goal = goal,
        Frametimes = baseline,
        TargetFps = ArgValue(args, "--target-fps") is { } t && int.TryParse(t, out var parsed) ? parsed : null,
    });
    // Prefer a setting from the advised plan; when none of those is bound to a
    // config file (e.g. the plan only contains menu-only settings), fall back to
    // the most efficient bound setting relevant to the bottleneck.
    var relevantImpacts = bottleneck switch
    {
        BottleneckType.Cpu => new[] { SettingImpact.Cpu, SettingImpact.Mixed },
        BottleneckType.Vram => [SettingImpact.Vram],
        BottleneckType.Ram => [SettingImpact.Ram, SettingImpact.Vram],
        _ => [SettingImpact.Gpu, SettingImpact.Mixed],
    };
    var setting = plan.Recommendations
                      .Select(r => r.Setting)
                      .FirstOrDefault(s => s?.Config is not null)
                  ?? game.Settings
                      .Where(s => s.Config is not null && relevantImpacts.Contains(s.Impact))
                      .OrderByDescending(s => s.PerformanceGain - s.QualityLoss)
                      .FirstOrDefault();
    if (setting is null)
        return Fail($"No {game.Name} setting relevant to a {bottleneck} bottleneck is bound to a " +
                    "config file yet — those changes must be made in the game's menu.");
    Console.WriteLine($"Testing ONE change: lower \"{setting.Name}\" by one step.");

    // 3. Apply (game must be closed), 4. re-measure, 5. keep or restore.
    if (!offline)
    {
        Console.WriteLine("Close the game now, then press Enter to apply the change...");
        Console.ReadLine();
        if (new RunningGameDetector().Detect([game]) is { } stillRunning)
            return Fail($"{game.Name} is still running (pid {stillRunning.ProcessId}).");
    }

    var backups = new ConfigBackupService(ConfigBackupService.DefaultBackupRoot());
    var loop = new OptimizationLoop(new SettingsApplier(backups), backups);
    var outcome = await loop.RunOnceAsync(game, setting, baseline, async () =>
    {
        if (offline)
            return FrametimeAnalyzer.Analyze(PresentMonCsv.ReadFrametimes(File.ReadAllText(afterCsv!)));

        if (!OperatingSystem.IsWindows())
            return null;
        Console.WriteLine("Change applied. Relaunch the game, return to the SAME scene, " +
                          "then press Enter to re-measure...");
        Console.ReadLine();
        var relaunched = new RunningGameDetector().Detect([game]);
        if (relaunched is null)
            return null;
        Console.WriteLine("Re-measuring for 30s...");
        return await PresentMonCapture.CaptureAsync(relaunched.ProcessId, TimeSpan.FromSeconds(30));
    });

    if (outcome is null)
        return Fail($"\"{setting.Name}\" could not be applied (value outside the documented ladder, " +
                    "or config file missing) — nothing was changed.");

    Console.WriteLine();
    Console.WriteLine($"Before : {outcome.Baseline.AverageFps:0.#} FPS avg, {outcome.Baseline.OnePercentLowFps:0.#} 1% low");
    Console.WriteLine($"After  : {outcome.After.AverageFps:0.#} FPS avg, {outcome.After.OnePercentLowFps:0.#} 1% low");
    Console.WriteLine(outcome.Verdict);
    if (!outcome.Kept)
        Console.WriteLine("Your previous settings are back in place.");
    return 0;
}

static string? ArgValue(string[] args, string flag)
{
    var index = Array.IndexOf(args, flag);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
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
