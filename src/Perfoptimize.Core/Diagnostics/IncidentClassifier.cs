namespace Perfoptimize.Core.Diagnostics;

/// <summary>
/// Turns raw event-log records into explained <see cref="SystemIncident"/>s:
/// what happened, the likely cause, and a concrete fix. Pure and fully
/// unit-testable — the Windows reader only feeds it normalized records.
///
/// Every branch answers the user's real question ("d'où ça vient, pourquoi,
/// comment corriger"), so the cause/fix text is deliberately specific to the
/// gaming context rather than generic.
/// </summary>
public static class IncidentClassifier
{
    private static readonly string[] GpuDriverMarkers = ["nvlddmkm", "amdkmdag", "amdwddm", "igfx", "igdkmd"];
    private static readonly string[] DiskSourceMarkers = ["disk", "ntfs", "volmgr", "storahci", "stornvme"];
    private static readonly int[] DiskCodes = [7, 9, 11, 51, 52, 153];

    /// <summary>Classifies and orders a batch of records, newest first, dropping unrecognized ones.</summary>
    public static IReadOnlyList<SystemIncident> ClassifyAll(IEnumerable<IncidentRecord> records)
        => records
            .Select(Classify)
            .OfType<SystemIncident>()
            .OrderByDescending(i => i.When ?? DateTimeOffset.MinValue)
            .ToList();

    /// <summary>Classifies a single record; null when it is not a stability incident we explain.</summary>
    public static SystemIncident? Classify(IncidentRecord r)
    {
        var source = r.Source ?? "";

        if (Eq(source, "Application Error"))
            return Crash(r);
        if (Eq(source, "Application Hang"))
            return Hang(r);
        if (Eq(source, "BugCheck") || Contains(source, "WER-SystemErrorReporting"))
            return BugCheck(r);
        if (r.EventCode == 41 || Contains(source, "Kernel-Power"))
            return Shutdown(r);
        if (r.EventCode == 4101 || Contains(source, "Display") || IsGpuDriver(source))
            return GpuReset(r);
        if (r.EventCode == 2004 || Contains(source, "Resource-Exhaustion"))
            return OutOfMemory(r);
        if (IsDisk(source) && DiskCodes.Contains(r.EventCode))
            return DiskError(r);
        // Windows Error Reporting is noisy and often duplicates an Application
        // Error; only keep it when it clearly names a crashed executable.
        if (Eq(source, "Windows Error Reporting") && FirstExe(r.InsertionStrings) is { } exe)
            return Crash(r, exe);

        return null;
    }

    private static SystemIncident Crash(IncidentRecord r, string? knownProcess = null)
    {
        var process = knownProcess ?? FirstExe(r.InsertionStrings);
        var module = r.InsertionStrings.Count > 3 && LooksLikeModule(r.InsertionStrings[3])
            ? r.InsertionStrings[3]
            : null;
        return new SystemIncident
        {
            Kind = IncidentKind.AppCrash,
            Severity = IncidentSeverity.Warning,
            When = r.When,
            Process = process,
            Title = process is null ? "Une application a planté" : $"{process} a planté",
            Detail = module is null
                ? "Arrêt anormal du processus."
                : $"Module fautif signalé : {module}.",
            LikelyCause = "Crash applicatif — le plus souvent un pilote GPU instable, une saturation " +
                          "mémoire/VRAM, ou un fichier de jeu corrompu.",
            SuggestedFix = "Mets à jour (ou réinstalle proprement) le pilote GPU, vérifie l'intégrité des " +
                           "fichiers du jeu (Steam : Propriétés → Fichiers installés → Vérifier), puis lance " +
                           "une analyse pour voir si la VRAM ou la RAM sature pendant le jeu.",
        };
    }

    private static SystemIncident Hang(IncidentRecord r)
    {
        var process = FirstExe(r.InsertionStrings);
        return new SystemIncident
        {
            Kind = IncidentKind.AppHang,
            Severity = IncidentSeverity.Warning,
            When = r.When,
            Process = process,
            Title = process is null ? "Une application s'est figée" : $"{process} s'est figé",
            Detail = "L'application a cessé de répondre puis a été fermée par Windows.",
            LikelyCause = "Le programme est resté bloqué : thread en attente, disque saturé à 100 %, " +
                          "ou attente d'une réponse du GPU.",
            SuggestedFix = "Regarde si un processus en arrière-plan sature le disque ou le CPU pendant le " +
                           "jeu (l'analyse continue du PC aide à l'attribuer), et évite de lancer des " +
                           "téléchargements/antivirus lourds en même temps.",
        };
    }

    private static SystemIncident GpuReset(IncidentRecord r)
    {
        var driver = IsGpuDriver(r.Source) ? r.Source : null;
        return new SystemIncident
        {
            Kind = IncidentKind.GpuReset,
            Severity = IncidentSeverity.Critical,
            When = r.When,
            Process = null,
            Title = "Plantage du pilote graphique (TDR)",
            Detail = driver is null
                ? "Le pilote d'affichage a cessé de répondre puis a récupéré."
                : $"Le pilote {driver} a cessé de répondre puis a récupéré.",
            LikelyCause = "TDR : le GPU n'a pas répondu à temps. Causes fréquentes — overclock GPU " +
                          "instable, surchauffe, pilote défaillant, ou alimentation insuffisante.",
            SuggestedFix = "Retire tout overclock GPU (MSI Afterburner remis à zéro), surveille la " +
                           "température GPU sous charge (l'analyse la mesure), réinstalle le pilote avec DDU, " +
                           "et vérifie que l'alimentation tient la charge.",
        };
    }

    private static SystemIncident BugCheck(IncidentRecord r)
    {
        var code = r.InsertionStrings.FirstOrDefault(s => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase));
        return new SystemIncident
        {
            Kind = IncidentKind.BugCheck,
            Severity = IncidentSeverity.Critical,
            When = r.When,
            Process = null,
            Title = "Écran bleu (BSOD)",
            Detail = code is null ? "Erreur noyau ayant provoqué un redémarrage." : $"Code d'arrêt : {code}.",
            LikelyCause = "Erreur au niveau du noyau : pilote défaillant, RAM instable (profil XMP/EXPO " +
                          "trop agressif), ou matériel défectueux.",
            SuggestedFix = "Note le code d'arrêt, teste la RAM (MemTest86), reviens aux réglages BIOS par " +
                           "défaut, puis mets à jour les pilotes chipset et GPU.",
        };
    }

    private static SystemIncident Shutdown(IncidentRecord r) => new()
    {
        Kind = IncidentKind.UnexpectedShutdown,
        Severity = IncidentSeverity.Critical,
        When = r.When,
        Process = null,
        Title = "Arrêt inattendu du PC",
        Detail = "Windows a redémarré sans s'être arrêté proprement.",
        LikelyCause = "Le PC s'est coupé net : coupure de courant, arrêt de protection par surchauffe, " +
                      "écran bleu non journalisé, ou alimentation défaillante.",
        SuggestedFix = "Vérifie les températures CPU/GPU sous charge, teste l'alimentation, et regarde si " +
                       "l'incident coïncide avec un écran bleu (ci-dessus) pour affiner la cause.",
    };

    private static SystemIncident OutOfMemory(IncidentRecord r) => new()
    {
        Kind = IncidentKind.OutOfMemory,
        Severity = IncidentSeverity.Warning,
        When = r.When,
        Process = FirstExe(r.InsertionStrings),
        Title = "Mémoire système saturée",
        Detail = "Windows a manqué de mémoire disponible.",
        LikelyCause = "Trop d'applications ouvertes en même temps, une fuite mémoire, ou un fichier " +
                      "d'échange trop petit.",
        SuggestedFix = "Ferme les applications en arrière-plan avant de jouer, augmente le fichier " +
                       "d'échange, et envisage plus de RAM (le conseiller matériel le signale si 16 Go).",
    };

    private static SystemIncident DiskError(IncidentRecord r) => new()
    {
        Kind = IncidentKind.DiskError,
        Severity = IncidentSeverity.Critical,
        When = r.When,
        Process = null,
        Title = "Erreur de disque signalée",
        Detail = r.InsertionStrings.Count > 0 ? $"Périphérique : {r.InsertionStrings[0]}." : "Erreur d'E/S disque.",
        LikelyCause = "Le disque a rapporté une erreur d'entrée/sortie : câble ou port SATA, secteurs " +
                      "défaillants, ou disque en fin de vie.",
        SuggestedFix = "Sauvegarde les données de ce disque en priorité, lance 'chkdsk', et vérifie l'état " +
                       "S.M.A.R.T. — un disque qui remonte des erreurs d'E/S provoque freezes et corruptions.",
    };

    // ---- helpers -----------------------------------------------------------
    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool Contains(string s, string part) => s.Contains(part, StringComparison.OrdinalIgnoreCase);
    private static bool IsGpuDriver(string source) => GpuDriverMarkers.Any(m => Contains(source, m));
    private static bool IsDisk(string source) => DiskSourceMarkers.Any(m => Eq(source, m) || Contains(source, m));

    private static string? FirstExe(IReadOnlyList<string> strings)
    {
        foreach (var s in strings)
        {
            if (!string.IsNullOrWhiteSpace(s) && s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return Path.GetFileName(s.Trim());
        }
        return null;
    }

    private static bool LooksLikeModule(string s)
        => !string.IsNullOrWhiteSpace(s) &&
           (s.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            s.EndsWith(".sys", StringComparison.OrdinalIgnoreCase));
}
