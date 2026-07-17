using Perfoptimize.Core.Startup;

namespace Perfoptimize.App.Endpoints;

/// <summary>
/// Prudent automation: the app proposes a change, previews it, backs it up and
/// only acts once the user confirms — never silently. The first concrete action
/// is disabling a startup entry (with a one-click way back).
/// </summary>
internal static class AutomationEndpoints
{
    public static void MapAutomationEndpoints(this WebApplication app)
    {
        // Disable a startup entry. Two-step by design: without confirm=true we
        // only return the preview (what will happen), never touching the machine.
        app.MapPost("/api/startup/disable", (StartupDisableRequest request) =>
        {
            if (!OperatingSystem.IsWindows())
                return Results.Json(new { ok = false, message = "Disponible uniquement sur Windows." });
            if (string.IsNullOrWhiteSpace(request.Id))
                return Results.Json(new { ok = false, message = "Aucune entrée indiquée." });

            var entry = new StartupScanner().Scan().FirstOrDefault(e => e.Id == request.Id);
            if (entry is null)
                return Results.Json(new { ok = false, message = "Entrée introuvable (peut-être déjà désactivée)." });

            var plan = StartupPlanner.Plan(entry);
            var planJson = new
            {
                plan.Id, plan.Name, location = plan.Location.ToString(), method = plan.Method.ToString(),
                plan.Action, plan.Reversal, plan.NeedsElevation, plan.Reversible,
            };

            // Propose + preview only.
            if (!request.Confirm)
                return Results.Json(new { ok = true, preview = true, plan = planJson });

            // Confirmed: back up + disable.
            var result = new StartupController().Disable(entry);
            return Results.Json(new { ok = result.Ok, message = result.Message, backupId = result.BackupId, plan = planJson });
        });

        // Re-enable a previously disabled entry from its backup.
        app.MapPost("/api/startup/restore", (StartupRestoreRequest request) =>
        {
            if (!OperatingSystem.IsWindows())
                return Results.Json(new { ok = false, message = "Disponible uniquement sur Windows." });
            if (string.IsNullOrWhiteSpace(request.Id))
                return Results.Json(new { ok = false, message = "Aucune entrée indiquée." });

            var result = new StartupController().Restore(request.Id);
            return Results.Json(new { ok = result.Ok, message = result.Message });
        });
    }
}

/// <summary>Body of a startup-disable request. Confirm gates the actual write.</summary>
internal sealed record StartupDisableRequest(string? Id, bool Confirm);

/// <summary>Body of a startup-restore (re-enable) request.</summary>
internal sealed record StartupRestoreRequest(string? Id);
