namespace Perfoptimize.Core.Startup;

/// <summary>
/// A plain-language description of what disabling an entry will do — the
/// "preview" the user sees and confirms before anything is touched. Prudent
/// automation: propose, preview, back up, confirm, then act. Never silent.
/// </summary>
public sealed record StartupActionPlan
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public StartupLocation Location { get; init; }
    public StartupDisableMethod Method { get; init; }
    /// <summary>What exactly happens (one line, French, user-facing).</summary>
    public string Action { get; init; } = "";
    /// <summary>How it is undone.</summary>
    public string Reversal { get; init; } = "";
    /// <summary>True when the change requires administrator rights to succeed.</summary>
    public bool NeedsElevation { get; init; }
    /// <summary>Reversible without data loss? Always true here — we only disable, never delete.</summary>
    public bool Reversible { get; init; } = true;
}

/// <summary>Pure builder for the preview shown before disabling a startup entry.</summary>
public static class StartupPlanner
{
    public static StartupActionPlan Plan(StartupEntry entry)
    {
        var method = StartupIdentity.MethodFor(entry.Location);
        var (action, reversal) = method switch
        {
            StartupDisableMethod.Registry => (
                "Retirer l'entrée de la clé de démarrage du registre (une sauvegarde est écrite avant).",
                "« Réactiver » réécrit l'entrée à l'identique depuis la sauvegarde."),
            StartupDisableMethod.StartupFile => (
                "Déplacer le fichier hors du dossier Démarrage vers un dossier de sauvegarde.",
                "« Réactiver » remet le fichier à sa place d'origine."),
            StartupDisableMethod.ScheduledTask => (
                "Désactiver la tâche planifiée (schtasks /Change /DISABLE) — elle n'est pas supprimée.",
                "« Réactiver » la ré-active (schtasks /Change /ENABLE)."),
            _ => ("Désactiver l'entrée.", "Réactiver depuis la sauvegarde."),
        };

        return new StartupActionPlan
        {
            Id = entry.Id,
            Name = entry.Name,
            Location = entry.Location,
            Method = method,
            Action = action,
            Reversal = reversal,
            NeedsElevation = StartupIdentity.NeedsElevation(entry.Location),
            Reversible = true,
        };
    }
}
