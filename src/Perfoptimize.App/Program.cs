using System.Reflection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Perfoptimize.App;
using Perfoptimize.Core.Configs;
using Perfoptimize.Core.Games;
using Perfoptimize.Core.Hardware;
using Perfoptimize.Core.Monitoring;
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
    var running = new RunningGameDetector().Detect(profiles);
    return Results.Json(new
    {
        known = profiles.Select(p => new { p.Name, executables = p.Executables, settings = p.Settings.Count }),
        running = running is null ? null : new { running.Profile.Name, running.ProcessId },
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
    var running = new RunningGameDetector().Detect(games);

    ISystemTelemetrySource source = OperatingSystem.IsWindows()
        ? new WindowsTelemetrySource(hardware.PrimaryGpu?.VramBytes)
        : PerformanceMonitor.CreateForCurrentPlatform();

    Task<FrametimeStatistics?>? presentMon = null;
    if (running is not null && OperatingSystem.IsWindows() && PresentMonCapture.FindExecutable() is not null)
        presentMon = PresentMonCapture.CaptureAsync(running.ProcessId, TimeSpan.FromSeconds(seconds));

    var report = await new PerformanceMonitor(source).CaptureAsync(
        TimeSpan.FromSeconds(seconds), gameProcessId: running?.ProcessId);
    var frametimes = presentMon is not null ? await presentMon : null;

    var summary = report.Summary;
    var analysis = BottleneckDetector.Analyze(summary,
        hardware.Cpu.LogicalCores > 0 ? hardware.Cpu.LogicalCores : Environment.ProcessorCount);
    var plan = RecommendationEngine.Advise(new AdviceRequest
    {
        Analysis = analysis, Game = running?.Profile, Goal = goal,
        Hardware = hardware, Summary = summary, Frametimes = frametimes, TargetFps = targetFps,
    });

    try
    {
        using var store = new SessionStore(SessionStore.DefaultDatabasePath());
        store.Save(new SessionRecord
        {
            StartedAt = DateTimeOffset.Now.AddSeconds(-seconds),
            GameName = running?.Profile.Name, Bottleneck = analysis.Type, Summary = summary,
            TargetFps = plan.TargetFps, AverageFps = frametimes?.AverageFps,
            OnePercentLowFps = frametimes?.OnePercentLowFps,
        });
    }
    catch { /* history is best-effort */ }

    return Results.Json(new
    {
        game = running?.Profile.Name,
        sampleCount = summary.SampleCount,
        primeSeconds = Math.Round(report.PrimeDuration.TotalSeconds, 1),
        cpu = new { avg = summary.AverageCpuUsagePercent, busiestCore = summary.AverageMaxCoreUsagePercent, gameShare = summary.AverageGameCpuUsagePercent },
        gpu = new { avg = summary.AverageGpuUsagePercent, gameShare = summary.AverageGameGpuUsagePercent, tempC = summary.PeakGpuTemperatureC, clockMhz = summary.AverageGpuCoreClockMhz },
        memory = new { ram = summary.PeakRamUsagePercent, vram = summary.PeakVramUsagePercent },
        fps = frametimes is null ? null : new { frametimes.AverageFps, frametimes.OnePercentLowFps, frametimes.StutterPercent },
        bottleneck = new { type = analysis.Type.ToString(), confidence = analysis.Confidence.ToString(), analysis.Explanation },
        target = new { plan.TargetFps, plan.MeasuredAverageFps, plan.RequiredGainPercent, gainLow = plan.EstimatedGainLowPercent, gainHigh = plan.EstimatedGainHighPercent },
        recommendations = plan.Recommendations.Select(r => new { r.Title, r.Reason, r.ExpectedGainPercent }),
        notes = plan.Notes,
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
        .Where(r => r.Setting?.Config is not null)
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
