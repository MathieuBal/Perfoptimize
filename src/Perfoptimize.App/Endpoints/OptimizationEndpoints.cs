using Perfoptimize.Core.Configs;
using Perfoptimize.Core.Games;
using Perfoptimize.Core.Optimization;
using Perfoptimize.Core.Storage;

namespace Perfoptimize.App.Endpoints;

/// <summary>Preview / apply / restore game config changes, with the CLI's safety guards.</summary>
internal static class OptimizationEndpoints
{
    public static void MapOptimizationEndpoints(this WebApplication app)
    {
        app.MapPost("/api/apply", (ApplyRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Game))
                return Results.Json(new { ok = false, message = "Aucun jeu n'est associé à cette analyse." });

            var profiles = GameProfileRepository.LoadBuiltIn();
            var game = profiles.FirstOrDefault(p =>
                           p.Name.Equals(request.Game, StringComparison.OrdinalIgnoreCase)) ??
                       profiles.FirstOrDefault(p =>
                           p.Name.Contains(request.Game, StringComparison.OrdinalIgnoreCase)) ??
                       RunningGameDetector.Match(request.Game, profiles);
            if (game is null)
                return Results.Json(new { ok = false, message = "Jeu inconnu." });

            // Writing must never happen while the game can overwrite its config on exit.
            if (request.Write && new RunningGameDetector().Detect([game]) is { } running)
                return Results.Json(new
                {
                    ok = false,
                    message = $"{game.Name} est en cours d'exécution (pid {running.ProcessId}) — ferme le jeu avant d'appliquer.",
                });

            SessionRecord? session;
            try
            {
                using var store = new SessionStore(SessionStore.DefaultDatabasePath());
                if (request.SessionId is { } requestedId)
                {
                    session = store.GetById(requestedId);
                    if (session is null)
                        return Results.Json(new { ok = false, message = $"La session #{requestedId} n'existe plus. Relance une analyse." });
                }
                else
                {
                    // Current UI compatibility: the analysis page does not yet
                    // post its session id, so select the newest session for this
                    // exact game. No plan is ever recomputed as a fallback.
                    session = store.GetRecent(limit: 1, game.Name).FirstOrDefault();
                }
            }
            catch
            {
                return Results.Json(new
                {
                    ok = false,
                    message = "Le plan exact de l'analyse est indisponible. Relance une analyse avant d'appliquer.",
                });
            }

            if (session is null)
                return Results.Json(new
                {
                    ok = false,
                    message = $"Aucune analyse enregistrée pour {game.Name}. Relance une analyse avant d'appliquer.",
                });
            if (!string.Equals(session.GameName, game.Name, StringComparison.OrdinalIgnoreCase))
                return Results.Json(new
                {
                    ok = false,
                    message = $"La session #{session.Id} appartient à {session.GameName ?? "une analyse système"}, pas à {game.Name}.",
                });
            if (!BottleneckTuning.IsTunable(session.Bottleneck))
                return Results.Json(new { ok = false, message = BottleneckTuning.RefusalReason(session.Bottleneck) });
            if (session.Plan.Count == 0)
                return Results.Json(new
                {
                    ok = false,
                    message = $"La session #{session.Id} ne contient pas de plan figé. Relance une analyse avant d'appliquer.",
                });

            // Dashboard writes are stricter than the CLI's legacy compatibility
            // path: only complete file/key/value snapshots are eligible. A plan
            // without them is refused instead of guessed from display names.
            var settings = StoredPlanMapper.ToExactSettings(session.Plan);
            if (settings.Count == 0)
                return Results.Json(new
                {
                    ok = false,
                    message = $"Aucune modification exacte et sûre n'a été enregistrée dans la session #{session.Id}. " +
                              "Les autres recommandations se règlent manuellement dans le jeu.",
                });

            var applier = new SettingsApplier(new ConfigBackupService(ConfigBackupService.DefaultBackupRoot()));
            var result = applier.Apply(game, settings, request.Write);
            return Results.Json(new
            {
                ok = true,
                sessionId = session.Id,
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
            if (string.IsNullOrWhiteSpace(request.Game))
                return Results.Json(new { ok = false, message = "Aucun jeu à restaurer." });

            var profiles = GameProfileRepository.LoadBuiltIn();
            var game = profiles.FirstOrDefault(p =>
                           p.Name.Equals(request.Game, StringComparison.OrdinalIgnoreCase)) ??
                       profiles.FirstOrDefault(p =>
                           p.Name.Contains(request.Game, StringComparison.OrdinalIgnoreCase)) ??
                       RunningGameDetector.Match(request.Game, profiles);
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
internal sealed record ApplyRequest(string? Game, string? Bottleneck, string? Goal, bool Write, long? SessionId = null);

/// <summary>Body of a restore request from the UI.</summary>
internal sealed record RestoreRequest(string? Game);
