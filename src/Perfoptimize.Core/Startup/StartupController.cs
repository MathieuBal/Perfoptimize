using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Perfoptimize.Core.Startup;

/// <summary>Outcome of a disable / restore action.</summary>
public sealed record StartupActionResult(bool Ok, string Message, string? BackupId = null);

/// <summary>
/// Turns a startup entry off (with a backup written first) and back on again.
/// This is the only part of the startup feature that writes to the machine, so
/// it is deliberately narrow: it only ever <em>disables</em> — never deletes —
/// and every disable is journalled so it can be reversed. All writes are
/// Windows-only and defensively wrapped; a permission failure becomes a clear
/// message, never an exception to the caller.
/// </summary>
public sealed class StartupController
{
    private readonly StartupBackupStore _store;

    public StartupController(StartupBackupStore store) => _store = store;

    public StartupController() : this(new StartupBackupStore(StartupBackupStore.DefaultDirectory())) { }

    /// <summary>Backs up then disables the entry. Idempotent-ish: a repeat is reported, not retried blindly.</summary>
    public StartupActionResult Disable(StartupEntry entry)
    {
        if (!OperatingSystem.IsWindows())
            return new StartupActionResult(false, "La désactivation n'est disponible que sur Windows.");
        if (_store.Find(entry.Id) is not null)
            return new StartupActionResult(true, "Déjà désactivé.", entry.Id);

        try
        {
            return StartupIdentity.MethodFor(entry.Location) switch
            {
                StartupDisableMethod.Registry => DisableRegistry(entry),
                StartupDisableMethod.StartupFile => DisableFile(entry),
                StartupDisableMethod.ScheduledTask => DisableTask(entry),
                _ => new StartupActionResult(false, "Type d'entrée non pris en charge."),
            };
        }
        catch (UnauthorizedAccessException)
        {
            return new StartupActionResult(false,
                "Accès refusé — relance Perfoptimize en administrateur pour agir sur cette entrée.");
        }
        catch (Exception ex)
        {
            return new StartupActionResult(false, $"Échec de la désactivation : {ex.Message}");
        }
    }

    /// <summary>Re-enables a previously disabled entry from its backup, then drops the backup.</summary>
    public StartupActionResult Restore(string id)
    {
        if (!OperatingSystem.IsWindows())
            return new StartupActionResult(false, "La restauration n'est disponible que sur Windows.");
        if (_store.Find(id) is not { } backup)
            return new StartupActionResult(false, "Aucune sauvegarde à restaurer pour cette entrée.");

        try
        {
            var result = backup.Method switch
            {
                StartupDisableMethod.Registry => RestoreRegistry(backup),
                StartupDisableMethod.StartupFile => RestoreFile(backup),
                StartupDisableMethod.ScheduledTask => RestoreTask(backup),
                _ => new StartupActionResult(false, "Type d'entrée non pris en charge."),
            };
            if (result.Ok)
                _store.Remove(id);
            return result;
        }
        catch (UnauthorizedAccessException)
        {
            return new StartupActionResult(false,
                "Accès refusé — relance Perfoptimize en administrateur pour réactiver cette entrée.");
        }
        catch (Exception ex)
        {
            return new StartupActionResult(false, $"Échec de la réactivation : {ex.Message}");
        }
    }

    // ---- Registry --------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private StartupActionResult DisableRegistry(StartupEntry entry)
    {
        var hive = HiveFor(entry.Location);
        var path = entry.Detail;
        if (hive is null || string.IsNullOrEmpty(path))
            return new StartupActionResult(false, "Emplacement du registre introuvable.");

        using var key = hive.OpenSubKey(path, writable: true);
        if (key is null)
            return new StartupActionResult(false, "Clé de démarrage inaccessible.");

        // Read the raw (unexpanded) value + its kind so restore is byte-identical.
        var raw = key.GetValue(entry.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
        if (raw is null)
            return new StartupActionResult(false, "L'entrée n'existe plus dans le registre.");
        var kind = key.GetValueKind(entry.Name);

        _store.Save(new StartupBackup
        {
            Id = entry.Id,
            Name = entry.Name,
            Command = raw,
            Location = entry.Location,
            Method = StartupDisableMethod.Registry,
            DisabledAt = DateTimeOffset.Now,
            RegistryKeyPath = path,
            RegistryValueName = entry.Name,
            RegistryValueKind = kind.ToString(),
        });

        key.DeleteValue(entry.Name);
        return new StartupActionResult(true, $"« {entry.Name} » retiré du démarrage (sauvegardé).", entry.Id);
    }

    [SupportedOSPlatform("windows")]
    private static StartupActionResult RestoreRegistry(StartupBackup backup)
    {
        var hive = HiveFor(backup.Location);
        if (hive is null || string.IsNullOrEmpty(backup.RegistryKeyPath) || backup.RegistryValueName is null)
            return new StartupActionResult(false, "Sauvegarde de registre incomplète.");

        using var key = hive.OpenSubKey(backup.RegistryKeyPath, writable: true)
            ?? hive.CreateSubKey(backup.RegistryKeyPath);
        if (key is null)
            return new StartupActionResult(false, "Clé de démarrage inaccessible.");

        var kind = Enum.TryParse<RegistryValueKind>(backup.RegistryValueKind, out var k) ? k : RegistryValueKind.String;
        key.SetValue(backup.RegistryValueName, backup.Command, kind);
        return new StartupActionResult(true, $"« {backup.Name} » réactivé au démarrage.");
    }

    [SupportedOSPlatform("windows")]
    private static RegistryKey? HiveFor(StartupLocation location) => location switch
    {
        StartupLocation.RegistryRunUser => Registry.CurrentUser,
        StartupLocation.RegistryRunMachine => Registry.LocalMachine,
        _ => null,
    };

    // ---- Startup folder --------------------------------------------------

    private StartupActionResult DisableFile(StartupEntry entry)
    {
        var original = entry.Command;
        if (!File.Exists(original))
            return new StartupActionResult(false, "Le fichier de démarrage n'existe plus.");

        var folder = _store.DisabledFilesDirectory(entry.Id);
        Directory.CreateDirectory(folder);
        var disabledPath = Path.Combine(folder, Path.GetFileName(original));
        File.Move(original, disabledPath, overwrite: true);

        _store.Save(new StartupBackup
        {
            Id = entry.Id,
            Name = entry.Name,
            Command = entry.Command,
            Location = entry.Location,
            Method = StartupDisableMethod.StartupFile,
            DisabledAt = DateTimeOffset.Now,
            OriginalPath = original,
            DisabledPath = disabledPath,
        });
        return new StartupActionResult(true, $"« {entry.Name} » retiré du dossier Démarrage (sauvegardé).", entry.Id);
    }

    private static StartupActionResult RestoreFile(StartupBackup backup)
    {
        if (string.IsNullOrEmpty(backup.DisabledPath) || string.IsNullOrEmpty(backup.OriginalPath))
            return new StartupActionResult(false, "Sauvegarde de fichier incomplète.");
        if (!File.Exists(backup.DisabledPath))
            return new StartupActionResult(false, "Le fichier sauvegardé est introuvable.");

        Directory.CreateDirectory(Path.GetDirectoryName(backup.OriginalPath)!);
        File.Move(backup.DisabledPath, backup.OriginalPath, overwrite: true);
        return new StartupActionResult(true, $"« {backup.Name} » remis dans le dossier Démarrage.");
    }

    // ---- Scheduled task --------------------------------------------------

    private StartupActionResult DisableTask(StartupEntry entry)
    {
        var taskPath = entry.Name; // scanner stores it as \Vendor\Task
        var (ok, output) = RunSchtasks(taskPath, enable: false);
        if (!ok)
            return new StartupActionResult(false, $"schtasks a échoué : {output}");

        _store.Save(new StartupBackup
        {
            Id = entry.Id,
            Name = entry.Name,
            Command = entry.Command,
            Location = entry.Location,
            Method = StartupDisableMethod.ScheduledTask,
            DisabledAt = DateTimeOffset.Now,
            TaskPath = taskPath,
        });
        return new StartupActionResult(true, $"Tâche « {entry.Name} » désactivée (sauvegardée).", entry.Id);
    }

    private static StartupActionResult RestoreTask(StartupBackup backup)
    {
        if (string.IsNullOrEmpty(backup.TaskPath))
            return new StartupActionResult(false, "Chemin de tâche manquant.");
        var (ok, output) = RunSchtasks(backup.TaskPath, enable: true);
        return ok
            ? new StartupActionResult(true, $"Tâche « {backup.Name} » réactivée.")
            : new StartupActionResult(false, $"schtasks a échoué : {output}");
    }

    private static (bool Ok, string Output) RunSchtasks(string taskPath, bool enable)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("/Change");
        psi.ArgumentList.Add("/TN");
        psi.ArgumentList.Add(taskPath);
        psi.ArgumentList.Add(enable ? "/ENABLE" : "/DISABLE");

        using var process = Process.Start(psi);
        if (process is null)
            return (false, "processus non démarré");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(15_000);
        var output = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        return (process.HasExited && process.ExitCode == 0, output.Trim());
    }
}
