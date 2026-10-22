using Perfoptimize.Core.Configs;
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

            // Conditional advisories are only trusted after crossing their rules
            // with the config that is actually on disk right now. An unreadable
            // or ambiguous key becomes CannotVerify, never an inferred warning.
            var configValues = ReadGameConfigValues(detected?.Profile);
            bool? targetMet = plan.RequiredGainPercent is double gap ? gap <= 0 : null;
            var advisoryChecks = AdvisoryEvaluator.EvaluateAll(plan.Advisories, new AdvisoryContext
            {
                ConfigValues = configValues,
                Bottleneck = analysis.Type,
                TargetMet = targetMet,
            });

            var visibleAdvisories = plan.Advisories
                .Where(a => a.Conditions.Count == 0)
                .Select(a => new AdvisoryResponse(
                    a.Id, a.Setting, a.Kind.ToString(), a.Recommendation, a.Detail,
                    a.Evidence.ToString(), "General", false, null,
                    "Avertissement général du profil ; son état actuel n'a pas été vérifié.",
                    a.LastVerified))
                .Concat(advisoryChecks
                    .Where(e => e.Status == AdvisoryStatus.Triggered)
                    .Select(e => new AdvisoryResponse(
                        e.Advisory.Id, e.Advisory.Setting, e.Advisory.Kind.ToString(),
                        e.Advisory.Recommendation, e.Advisory.Detail,
                        e.Advisory.Evidence.ToString(), e.Status.ToString(), e.HighPriority,
                        e.CurrentValue, e.Explanation, e.Advisory.LastVerified)))
                .ToList();

            // Freeze the concrete plan now, while the values the user just saw
            // still describe the config. Applying later must replay these exact
            // edits; it must never reconstruct a new plan from only a bottleneck.
            var storedPlan = StoredPlanMapper.Snapshot(plan, detected?.Profile);
            var exactSettings = StoredPlanMapper.ToExactSettings(storedPlan);
            long? sessionId = null;
            try
            {
                using var store = new SessionStore(SessionStore.DefaultDatabasePath());
                sessionId = store.Save(new SessionRecord
                {
                    StartedAt = DateTimeOffset.Now.AddSeconds(-seconds),
                    GameName = detected?.Name, Bottleneck = analysis.Type, Summary = summary,
                    TargetFps = plan.TargetFps, AverageFps = frametimes?.AverageFps,
                    OnePercentLowFps = frametimes?.OnePercentLowFps,
                    Plan = storedPlan,
                });
            }
            catch { /* history is best-effort; without a saved session the dashboard will refuse writes */ }

            return Results.Json(new
            {
                sessionId,
                game = detected?.Name,
                gameKnown = detected?.IsKnown ?? false,
                canApply = sessionId is not null && detected?.Profile is not null && exactSettings.Count > 0 &&
                           BottleneckTuning.IsTunable(analysis.Type),
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
                baselineFpsMeasured = frametimes is not null,
                gainsAreMeasured = false,
                notes = plan.Notes,
                advisories = visibleAdvisories,
                // Full check state is returned for transparency/debugging, while
                // the normal advisories list only exposes triggered conditional
                // warnings plus legacy general guidance.
                advisoryChecks = advisoryChecks.Select(e => new
                {
                    id = e.Advisory.Id,
                    setting = e.Advisory.Setting,
                    status = e.Status.ToString(),
                    e.HighPriority,
                    e.CurrentValue,
                    e.Explanation,
                }),
            });
        });

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

    private static IReadOnlyDictionary<string, string> ReadGameConfigValues(GameProfile? game)
    {
        if (game is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var config in game.ConfigFiles)
        {
            try
            {
                var path = config.ResolvePath();
                if (!File.Exists(path))
                    continue;

                var parsed = ConfigValueReader.Read(File.ReadAllText(path), config.Format.ToString());
                foreach (var (key, value) in parsed)
                {
                    if (ambiguous.Contains(key))
                        continue;
                    if (values.TryGetValue(key, out var existing) &&
                        !string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
                    {
                        values.Remove(key);
                        ambiguous.Add(key);
                    }
                    else
                    {
                        values[key] = value;
                    }
                }
            }
            catch
            {
                // Reading config is advisory-only and must never make a monitor
                // session fail. Missing data simply produces CannotVerify.
            }
        }

        return values;
    }
}

internal sealed record MonitorRequest(int? Seconds, string? Goal, int? TargetFps);
internal sealed record AdvisoryResponse(
    string Id,
    string Setting,
    string Kind,
    string Recommendation,
    string Detail,
    string Evidence,
    string Status,
    bool HighPriority,
    string? CurrentValue,
    string Explanation,
    string? LastVerified);
