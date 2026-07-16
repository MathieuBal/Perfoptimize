using Perfoptimize.Core.Configs;
using Perfoptimize.Core.Games;
using Perfoptimize.Core.Optimization;

namespace Perfoptimize.App.Endpoints;

/// <summary>Preview / apply / restore game config changes, with the CLI's safety guards.</summary>
internal static class OptimizationEndpoints
{
    public static void MapOptimizationEndpoints(this WebApplication app)
    {
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
    }
}

/// <summary>Body of a preview/apply request from the UI.</summary>
internal sealed record ApplyRequest(string? Game, string? Bottleneck, string? Goal, bool Write);

/// <summary>Body of a restore request from the UI.</summary>
internal sealed record RestoreRequest(string? Game);
