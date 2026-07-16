using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Perfoptimize.Core.Startup;

/// <summary>
/// Lists everything that runs on its own at logon or on a schedule: the Run
/// registry keys, the Startup folders, and non-Microsoft scheduled tasks. This
/// is where a background updater that "flashes a window and freezes the PC"
/// hides. Read-only and defensive — any inaccessible key/file is skipped.
/// </summary>
public sealed class StartupScanner
{
    public IReadOnlyList<StartupEntry> Scan() =>
        OperatingSystem.IsWindows() ? ScanWindows() : [];

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<StartupEntry> ScanWindows()
    {
        var entries = new List<StartupEntry>();

        AddRunKey(entries, Registry.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Run", StartupLocation.RegistryRunUser);
        AddRunKey(entries, Registry.LocalMachine,
            @"Software\Microsoft\Windows\CurrentVersion\Run", StartupLocation.RegistryRunMachine);
        AddRunKey(entries, Registry.LocalMachine,
            @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", StartupLocation.RegistryRunMachine);

        AddStartupFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.Startup));
        AddStartupFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));

        AddScheduledTasks(entries);
        return entries;
    }

    [SupportedOSPlatform("windows")]
    private static void AddRunKey(List<StartupEntry> entries, RegistryKey hive, string path, StartupLocation location)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key is null)
                return;
            foreach (var name in key.GetValueNames())
            {
                var command = key.GetValue(name)?.ToString();
                if (string.IsNullOrWhiteSpace(command))
                    continue;
                entries.Add(new StartupEntry
                {
                    Name = string.IsNullOrWhiteSpace(name) ? command : name,
                    Command = command,
                    Location = location,
                    Detail = path,
                });
            }
        }
        catch
        {
            // Restricted key — skip it.
        }
    }

    private static void AddStartupFolder(List<StartupEntry> entries, string folder)
    {
        try
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return;
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    continue;
                entries.Add(new StartupEntry
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Command = file,
                    Location = StartupLocation.StartupFolder,
                    Detail = folder,
                });
            }
        }
        catch
        {
            // Folder not accessible — skip.
        }
    }

    private static void AddScheduledTasks(List<StartupEntry> entries)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks");
            if (!Directory.Exists(root))
                return;

            foreach (var file in SafeEnumerateFiles(root))
            {
                var relative = Path.GetRelativePath(root, file);
                // Skip Windows' own tasks — we only surface third-party ones.
                if (relative.StartsWith("Microsoft" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;

                string xml;
                try { xml = File.ReadAllText(file); }
                catch { continue; }

                if (ScheduledTaskParser.Parse(xml) is not { } task)
                    continue;

                var command = task.Arguments is null ? task.Command! : $"{task.Command} {task.Arguments}";
                entries.Add(new StartupEntry
                {
                    Name = "\\" + relative.Replace(Path.DirectorySeparatorChar, '\\'),
                    Command = command,
                    Location = StartupLocation.ScheduledTask,
                    Detail = task.Author,
                });
            }
        }
        catch
        {
            // Task store not accessible (needs elevation for some subtrees) — best effort.
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
        }
        catch
        {
            return [];
        }
    }
}
