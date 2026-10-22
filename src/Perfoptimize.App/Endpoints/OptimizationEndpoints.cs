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
                    // Compatibility for non-cockpit clients. The cockpit sends
                    // the exact session id; no plan is ever recomputed here.
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
            ApplyResult result;
            try
            {
                // A write from the cockpit is an exact frozen transaction at the
                // planning boundary: if one config value changed since analysis,
                // the whole plan is refused instead of applying only a subset.
                result = applier.Apply(game, settings, request.Write, requireAllReady: request.Write);
            }
            catch
            {
                return Results.Json(new
                {
                    ok = false,
                    sessionId = session.Id,
                    game = game.Name,
                    written = false,
                    message = request.Write
                        ? "L'application a échoué. Les fichiers déjà modifiés pendant cette tentative ont été restaurés depuis leurs sauvegardes."
                        : "Impossible de relire la configuration pour prévisualiser ce plan.",
                });
            }

            var changes = result.Changes.Select(c => new
            {
                setting = c.Setting.Name,
                status = c.Status.ToString(),
                note = c.Note,
                current = c.CurrentValue,
                proposed = c.NewValue,
            }).ToList();

            if (request.Write && !result.Written)
            {
                return Results.Json(new
                {
                    ok = false,
                    sessionId = session.Id,
                    game = game.Name,
                    written = false,
                    message = "La configuration a changé depuis l'analyse : aucune modification n'a été appliquée. Relance l'analyse pour obtenir un plan à jour.",
                    changes,
                    backups = Array.Empty<string>(),
                });
            }

            return Results.Json(new
            {
                ok = true,
                sessionId = session.Id,
                game = game.Name,
                written = result.Written,
                changes,
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

            var backupService = new ConfigBackupService(ConfigBackupService.DefaultBackupRoot());

            if (request.Exact)
            {
                if (request.Backups is not { Count: > 0 })
                    return Results.Json(new
                    {
                        ok = false,
                        message = "La sauvegarde exacte de cette application est indisponible. Aucune restauration n'a été tentée.",
                    });

                // Validate the whole restore plan before writing anything. A
                // client path must correspond to a backup actually created by
                // Perfoptimize for one of THIS game's documented config files.
                var restorePlan = new List<(string ConfigPath, string BackupPath)>();
                foreach (var requestedBackup in request.Backups.Distinct(PathComparer))
                {
                    var configPath = game.ConfigFiles
                        .Select(file => file.ResolvePath())
                        .FirstOrDefault(path => backupService.IsKnownBackup(game.Name, path, requestedBackup));
                    if (configPath is null)
                    {
                        return Results.Json(new
                        {
                            ok = false,
                            message = "Une sauvegarde demandée ne correspond pas à ce jeu. Aucune restauration n'a été tentée.",
                        });
                    }
                    if (restorePlan.Any(item => PathComparer.Equals(item.ConfigPath, configPath)))
                    {
                        return Results.Json(new
                        {
                            ok = false,
                            message = "Plusieurs sauvegardes ont été fournies pour le même fichier. Aucune restauration n'a été tentée.",
                        });
                    }
                    restorePlan.Add((configPath, requestedBackup));
                }

                var restored = new List<string>();
                foreach (var (configPath, backupPath) in restorePlan)
                {
                    if (backupService.RestoreExact(game.Name, configPath, backupPath) is null)
                    {
                        return Results.Json(new
                        {
                            ok = false,
                            message = "La sauvegarde exacte n'est plus disponible. La restauration a été interrompue.",
                            restored,
                        });
                    }
                    restored.Add(configPath);
                }

                return Results.Json(new
                {
                    ok = true,
                    exact = true,
                    message = $"{restored.Count} fichier(s) restauré(s) depuis les sauvegardes exactes de cette application.",
                    restored,
                });
            }

            // Manual/history restore keeps the previous behavior: restore the
            // most recent known backup for each documented config file.
            var latestRestored = new List<string>();
            foreach (var configFile in game.ConfigFiles)
            {
                var path = configFile.ResolvePath();
                if (backupService.RestoreLatest(game.Name, path) is not null)
                    latestRestored.Add(path);
            }
            return Results.Json(latestRestored.Count > 0
                ? new { ok = true, exact = false, message = $"{latestRestored.Count} fichier(s) restauré(s) depuis la dernière sauvegarde." }
                : new { ok = false, exact = false, message = "Aucune sauvegarde à restaurer pour ce jeu." });
        });
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

/// <summary>Body of a preview/apply request from the UI.</summary>
internal sealed record ApplyRequest(string? Game, string? Bottleneck, string? Goal, bool Write, long? SessionId = null);

/// <summary>Body of a restore request from the UI.</summary>
internal sealed record RestoreRequest(string? Game, IReadOnlyList<string>? Backups = null, bool Exact = false);
