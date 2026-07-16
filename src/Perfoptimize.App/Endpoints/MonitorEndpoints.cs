using Perfoptimize.Core.Games;
using Perfoptimize.Core.Hardware;
using Perfoptimize.Core.Monitoring;
using Perfoptimize.Core.Monitoring.Frametimes;
using Perfoptimize.Core.Monitoring.Windows;
using Perfoptimize.Core.Optimization;
using Perfoptimize.Core.Storage;

namespace Perfoptimize.App.Endpoints;

/// <summary>Game optimization: measure/diagnose/advise, and explain the settings.</summary>
internal static class MonitorEndpoints
{
    public static void MapMonitorEndpoints(this WebApplication app)
    {
        // Monitoring: measure, diagnose, advise.
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

        // Settings guide: explain what each setting does + DX11/DX12/Vulkan choice.
        app.MapGet("/api/settings-guide", (string? bottleneck) =>
        {
            Enum.TryParse<BottleneckType>(bottleneck, ignoreCase: true, out var type);
            var detected = new RunningGameDetector().DetectAny(GameProfileRepository.LoadBuiltIn());
            var renderer = SettingsGlossary.RecommendRenderer(type);
            return Results.Json(new
            {
                game = detected?.Name,
                gameKnown = detected?.IsKnown ?? false,
                renderer = new
                {
                    recommendedId = renderer.OptionId,
                    recommendation = renderer.Text,
                    options = SettingsGlossary.Renderers.Select(r => new { r.Id, r.Name, r.Summary, r.BestWhen }),
                },
                settings = SettingsGlossary.Entries.Select(e => new
                {
                    e.Id, e.Title, e.WhatItChanges, cost = e.Cost.ToString(), e.Guidance,
                }),
            });
        });
    }
}

/// <summary>Body of a monitoring request from the UI.</summary>
internal sealed record MonitorRequest(int? Seconds, string? Goal, int? TargetFps);
