using System.Reflection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Perfoptimize.App;
using Perfoptimize.Core.Configs;
using Perfoptimize.Core.Diagnostics;
using Perfoptimize.Core.Games;
using Perfoptimize.Core.Hardware;
using Perfoptimize.Core.Library;
using Perfoptimize.Core.Live;
using Perfoptimize.Core.Monitoring;
using Perfoptimize.Core.Network;
using Perfoptimize.Core.Monitoring.Frametimes;
using Perfoptimize.Core.Monitoring.Windows;
using Perfoptimize.Core.Optimization;
using Perfoptimize.Core.Storage;
using Perfoptimize.Core.Tools;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); // a desktop tool should not spam the console
// Ephemeral loopback port: no conflict with anything the user already runs.
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddHttpClient();

var app = builder.Build();

// ----- UI (embedded assets, single-file safe) --------------------------------
app.MapGet("/", () => Results.Content(WebAssets.Read("index.html"), "text/html; charset=utf-8"));
app.MapGet("/app.js", () => Results.Content(WebAssets.Read("app.js"), "text/javascript; charset=utf-8"));
app.MapGet("/styles.css", () => Results.Content(WebAssets.Read("styles.css"), "text/css; charset=utf-8"));

// ----- Read-only diagnostics -------------------------------------------------
app.MapGet("/api/hardware", () =>
{
    var profile = new HardwareScanner().Scan();
    return Results.Json(new
    {
        cpu = new { profile.Cpu.Name, profile.Cpu.PhysicalCores, profile.Cpu.LogicalCores, profile.Cpu.MaxClockMhz },
        gpus = profile.Gpus.Select(g => new { g.Name, g.Vendor, g.IsDiscrete, g.VramBytes, g.DriverVersion }),
        primaryGpu = profile.PrimaryGpu?.Name,
        memory = new { profile.Memory.TotalBytes, profile.Memory.AvailableBytes, profile.Memory.SpeedMtPerSecond },
        displays = profile.Displays.Select(d => new { d.Name, d.WidthPixels, d.HeightPixels, d.RefreshRateHz, d.IsPrimary }),
        storage = profile.StorageDevices.Select(s => new { s.Model, MediaType = s.MediaType.ToString(), s.TotalBytes }),
        os = new { profile.OperatingSystem.Name, profile.OperatingSystem.Version, profile.OperatingSystem.Architecture },
    });
});

app.MapGet("/api/games", () =>
{
    var profiles = GameProfileRepository.LoadBuiltIn();
    var detected = new RunningGameDetector().DetectAny(profiles);
    return Results.Json(new
    {
        known = profiles.Select(p => new { p.Name, executables = p.Executables, settings = p.Settings.Count }),
        running = detected is null ? null : new
        {
            detected.Name, detected.ProcessId, detected.IsKnown, detected.Reasons,
        },
    });
});

app.MapGet("/api/system", () =>
{
    var profile = new HardwareScanner().Scan();
    var advice = SystemAdvisor.Analyze(profile);
    return Results.Json(new
    {
        advice = advice.Select(a => new { a.Category, a.Title, a.Detail, severity = a.Severity.ToString() }),
    });
});

// ----- Library: discover installed games and read their config files ---------
app.MapGet("/api/library", () =>
{
    var games = new InstalledGameScanner().Scan();
    return Results.Json(new
    {
        games = games.Select(g => new { g.Name, g.InstallDir, source = g.Source.ToString(), g.AppId }),
    });
});

app.MapGet("/api/library/config", (string dir) =>
{
    // Only read config files of a game we actually discovered installed —
    // never an arbitrary path handed to us by the client.
    var game = new InstalledGameScanner().Scan()
        .FirstOrDefault(g => string.Equals(g.InstallDir, dir, StringComparison.OrdinalIgnoreCase));
    if (game is null)
        return Results.Json(new { ok = false, message = "Jeu non trouvé dans la bibliothèque." });

    var configs = new GameConfigScanner().FindConfigs(game.Name, game.InstallDir);
    return Results.Json(new
    {
        ok = true,
        game = game.Name,
        configs = configs.Select(c => new
        {
            c.Path, c.Format, c.TotalKeys,
            settings = c.GraphicsSettings.Select(s => new { s.Section, s.Key, s.Value }),
        }),
    });
});

// ----- Monitoring: measure, diagnose, advise ---------------------------------
app.MapPost("/api/monitor", async (MonitorRequest? request) =>
{
    var seconds = Math.Clamp(request?.Seconds ?? 30, 3, 120);
    var goal = Enum.TryParse<OptimizationGoal>(request?.Goal, ignoreCase: true, out var g) ? g : OptimizationGoal.Balanced;
    int? targetFps = request?.TargetFps is > 0 ? request.TargetFps : null;

    var hardware = new HardwareScanner().Scan();
    var games = GameProfileRepository.LoadBuiltIn();
    var detected = new RunningGameDetector().DetectAny(games);

    ISystemTelemetrySource source = OperatingSystem.IsWindows()
        ? new WindowsTelemetrySource(hardware.PrimaryGpu?.VramBytes)
        : PerformanceMonitor.CreateForCurrentPlatform();

    Task<FrametimeStatistics?>? presentMon = null;
    if (detected is not null && OperatingSystem.IsWindows() && PresentMonCapture.FindExecutable() is not null)
        presentMon = PresentMonCapture.CaptureAsync(detected.ProcessId, TimeSpan.FromSeconds(seconds));

    var report = await new PerformanceMonitor(source).CaptureAsync(
        TimeSpan.FromSeconds(seconds), gameProcessId: detected?.ProcessId);
    var frametimes = presentMon is not null ? await presentMon : null;

    var summary = report.Summary;
    var analysis = BottleneckDetector.Analyze(summary,
        hardware.Cpu.LogicalCores > 0 ? hardware.Cpu.LogicalCores : Environment.ProcessorCount);
    var plan = RecommendationEngine.Advise(new AdviceRequest
    {
        Analysis = analysis, Game = detected?.Profile, Goal = goal,
        Hardware = hardware, Summary = summary, Frametimes = frametimes, TargetFps = targetFps,
    });

    try
    {
        using var store = new SessionStore(SessionStore.DefaultDatabasePath());
        store.Save(new SessionRecord
        {
            StartedAt = DateTimeOffset.Now.AddSeconds(-seconds),
            GameName = detected?.Name, Bottleneck = analysis.Type, Summary = summary,
            TargetFps = plan.TargetFps, AverageFps = frametimes?.AverageFps,
            OnePercentLowFps = frametimes?.OnePercentLowFps,
        });
    }
    catch { /* history is best-effort */ }

    return Results.Json(new
    {
        game = detected?.Name,
        gameKnown = detected?.IsKnown ?? false,
        canApply = detected?.Profile is not null,
        sampleCount = summary.SampleCount,
        primeSeconds = Math.Round(report.PrimeDuration.TotalSeconds, 1),
        cpu = new { avg = summary.AverageCpuUsagePercent, busiestCore = summary.AverageMaxCoreUsagePercent, gameShare = summary.AverageGameCpuUsagePercent },
        gpu = new { avg = summary.AverageGpuUsagePercent, gameShare = summary.AverageGameGpuUsagePercent, tempC = summary.PeakGpuTemperatureC, clockMhz = summary.AverageGpuCoreClockMhz },
        memory = new { ram = summary.PeakRamUsagePercent, vram = summary.PeakVramUsagePercent },
        fps = frametimes is null ? null : new
        {
            frametimes.AverageFps, frametimes.OnePercentLowFps, frametimes.StutterPercent,
            frametimes.WorstFrametimeMs, series = frametimes.FpsSeries,
        },
        bottleneck = new { type = analysis.Type.ToString(), confidence = analysis.Confidence.ToString(), analysis.Explanation },
        target = new { plan.TargetFps, plan.MeasuredAverageFps, plan.RequiredGainPercent, gainLow = plan.EstimatedGainLowPercent, gainHigh = plan.EstimatedGainHighPercent },
        recommendations = plan.Recommendations.Select(r => new
        {
            r.Title, r.Reason, r.ExpectedGainPercent, evidence = r.Evidence.ToString(),
        }),
        // Whether any gain is measured on this machine: without frametimes, every
        // number is theoretical and must be shown as such.
        gainsAreMeasured = frametimes is not null,
        notes = plan.Notes,
        // Community-documented "settings to avoid" for this game (crashes / stutter).
        advisories = plan.Advisories.Select(a => new
        {
            a.Setting, kind = a.Kind.ToString(), a.Recommendation, a.Detail, evidence = a.Evidence.ToString(),
        }),
    });
});

// ----- Live: continuous whole-PC monitoring ----------------------------------
app.MapGet("/api/live", async () =>
{
    // One instant of the whole machine: system CPU/RAM, the busiest processes,
    // and what is competing with the running game right now. The UI polls this.
    var profiles = GameProfileRepository.LoadBuiltIn();
    var detected = new RunningGameDetector().DetectAny(profiles);

    var snapshot = await new LiveMonitor().SampleAsync(
        TimeSpan.FromMilliseconds(350), detected?.ProcessId, detected?.Name);

    return Results.Json(new
    {
        timestamp = snapshot.Timestamp.ToString("o"),
        snapshot.SystemCpuPercent,
        snapshot.RamUsedPercent,
        snapshot.RamUsedBytes,
        snapshot.RamTotalBytes,
        snapshot.ProcessCount,
        game = snapshot.GameName,
        snapshot.GamePid,
        topByCpu = snapshot.TopByCpu.Select(u => new { u.Pid, u.Name, u.CpuPercent, u.MemoryBytes, u.MemoryPercent, u.IsGame }),
        topByMemory = snapshot.TopByMemory.Select(u => new { u.Pid, u.Name, u.CpuPercent, u.MemoryBytes, u.MemoryPercent, u.IsGame }),
        alerts = snapshot.Alerts.Select(a => new { kind = a.Kind.ToString(), a.Level, a.Title, a.Detail }),
    });
});

// ----- Network: what is connected and listening ------------------------------
app.MapGet("/api/network", () =>
{
    // Read-only inventory of TCP activity: established connections grouped by
    // remote host, and the ports open for listening.
    var snapshot = new NetworkReader().Read();
    return Results.Json(new
    {
        snapshot.EstablishedCount,
        snapshot.ListeningCount,
        topRemotes = snapshot.TopRemotes.Select(r => new { r.Address, r.Connections }),
        listeningPorts = snapshot.ListeningPorts,
    });
});

// ----- Diagnostics: explain recent freezes / crashes -------------------------
app.MapGet("/api/incidents", () =>
{
    // Read-only post-mortem: reads the Windows event history and explains each
    // stability incident (crash, hang, GPU reset, BSOD, unexpected shutdown,
    // out-of-memory, disk error) with a likely cause and a concrete fix.
    if (!OperatingSystem.IsWindows())
        return Results.Json(new { supported = false, incidents = Array.Empty<object>() });

    var records = new WindowsEventLogReader().ReadRecent(TimeSpan.FromDays(14));
    var incidents = IncidentClassifier.ClassifyAll(records).Take(60);
    return Results.Json(new
    {
        supported = true,
        incidents = incidents.Select(i => new
        {
            kind = i.Kind.ToString(),
            severity = i.Severity.ToString(),
            when = i.When?.ToString("o"),
            i.Process,
            i.Title,
            i.Detail,
            i.LikelyCause,
            i.SuggestedFix,
        }),
    });
});

// ----- Tools: PresentMon install from the UI ---------------------------------
app.MapGet("/api/tools", () =>
{
    var path = PresentMonInstaller.FindInstalled();
    return Results.Json(new
    {
        presentMon = new { installed = path is not null, path },
        platform = new { isWindows = OperatingSystem.IsWindows() },
    });
});

app.MapPost("/api/tools/presentmon", async (IHttpClientFactory factory) =>
{
    if (!OperatingSystem.IsWindows())
        return Results.Json(new { success = false, message = "PresentMon is a Windows tool." });
    var installer = new PresentMonInstaller(factory.CreateClient());
    var result = await installer.InstallLatestAsync();
    if (result.Success && result.ExecutablePath is not null)
        Environment.SetEnvironmentVariable("PERFOPTIMIZE_PRESENTMON", result.ExecutablePath);
    return Results.Json(new { result.Success, result.Message, result.ExecutablePath, result.Version });
});

// ----- Optimization: preview / apply / restore config from the UI ------------
app.MapPost("/api/apply", (ApplyRequest request) =>
{
    var profiles = GameProfileRepository.LoadBuiltIn();
    var game = profiles.FirstOrDefault(p =>
                   p.Name.Contains(request.Game ?? "", StringComparison.OrdinalIgnoreCase)) ??
               RunningGameDetector.Match(request.Game ?? "", profiles);
    if (game is null)
        return Results.Json(new { ok = false, message = "Jeu inconnu." });

    if (!Enum.TryParse<BottleneckType>(request.Bottleneck, ignoreCase: true, out var bottleneck))
        bottleneck = BottleneckType.Gpu;
    // The same protection as the advisor/CLI: never touch settings for verdicts
    // a setting change cannot fix.
    if (!BottleneckTuning.IsTunable(bottleneck))
        return Results.Json(new { ok = false, message = BottleneckTuning.RefusalReason(bottleneck) });

    var goal = Enum.TryParse<OptimizationGoal>(request.Goal, ignoreCase: true, out var g)
        ? g : OptimizationGoal.Balanced;

    // Writing must never happen while the game can overwrite its config on exit.
    if (request.Write && new RunningGameDetector().Detect([game]) is { } running)
        return Results.Json(new
        {
            ok = false,
            message = $"{game.Name} est en cours d'exécution (pid {running.ProcessId}) — ferme le jeu avant d'appliquer.",
        });

    var settings = RecommendationEngine
        .Recommend(new BottleneckAnalysis { Type = bottleneck }, game, goal)
        // Only auto-lowerable settings may be written — never a KeepEnabled or
        // TestAlternatives setting, even if it carries a config binding.
        .Where(r => r.Setting is { Config: not null } s && s.IsAutoLowerable)
        .Select(r => r.Setting!)
        .ToList();
    if (settings.Count == 0)
        return Results.Json(new
        {
            ok = false,
            message = $"Aucun réglage de {game.Name} lié à un fichier de config pour un goulot {bottleneck} " +
                      "(ces réglages se changent dans le menu du jeu).",
        });

    var applier = new SettingsApplier(new ConfigBackupService(ConfigBackupService.DefaultBackupRoot()));
    var result = applier.Apply(game, settings, request.Write);
    return Results.Json(new
    {
        ok = true,
        game = game.Name,
        written = result.Written,
        changes = result.Changes.Select(c => new
        {
            setting = c.Setting.Name,
            status = c.Status.ToString(),
            note = c.Note,
            current = c.CurrentValue,
            proposed = c.NewValue,
        }),
        backups = result.BackupPaths,
    });
});

app.MapPost("/api/restore", (RestoreRequest request) =>
{
    var profiles = GameProfileRepository.LoadBuiltIn();
    var game = profiles.FirstOrDefault(p =>
                   p.Name.Contains(request.Game ?? "", StringComparison.OrdinalIgnoreCase)) ??
               RunningGameDetector.Match(request.Game ?? "", profiles);
    if (game is null)
        return Results.Json(new { ok = false, message = "Jeu inconnu." });
    if (new RunningGameDetector().Detect([game]) is { } running)
        return Results.Json(new
        {
            ok = false,
            message = $"{game.Name} est en cours d'exécution (pid {running.ProcessId}) — ferme le jeu avant de restaurer.",
        });

    var backups = new ConfigBackupService(ConfigBackupService.DefaultBackupRoot());
    var restored = new List<string>();
    foreach (var configFile in game.ConfigFiles)
    {
        var path = configFile.ResolvePath();
        if (backups.RestoreLatest(game.Name, path) is not null)
            restored.Add(path);
    }
    return Results.Json(restored.Count > 0
        ? new { ok = true, message = $"{restored.Count} fichier(s) restauré(s) depuis la dernière sauvegarde." }
        : new { ok = false, message = "Aucune sauvegarde à restaurer pour ce jeu." });
});

app.MapGet("/api/version", () => Results.Json(new
{
    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev",
}));

app.MapPost("/api/shutdown", (IHostApplicationLifetime life) =>
{
    // Fire after the response is sent so the browser sees the goodbye.
    _ = Task.Run(async () => { await Task.Delay(300); life.StopApplication(); });
    return Results.Ok();
});

// ----- Launch: print the URL and open the browser ----------------------------
app.Lifetime.ApplicationStarted.Register(() =>
{
    var address = app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault() ?? "http://127.0.0.1:5000";
    Console.WriteLine($"Perfoptimize is running at {address}");
    Console.WriteLine("Keep this window open while you use the app. Close it (or use Quit in the UI) to stop.");
    BrowserLauncher.Open(address);
});

app.Run();

/// <summary>Body of a monitoring request from the UI.</summary>
internal sealed record MonitorRequest(int? Seconds, string? Goal, int? TargetFps);

/// <summary>Body of a preview/apply request from the UI.</summary>
internal sealed record ApplyRequest(string? Game, string? Bottleneck, string? Goal, bool Write);

/// <summary>Body of a restore request from the UI.</summary>
internal sealed record RestoreRequest(string? Game);
