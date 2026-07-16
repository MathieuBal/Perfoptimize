using System.Diagnostics;

namespace Perfoptimize.Core.Network;

/// <summary>
/// Lists the machine's established outbound connections attributed to the owning
/// program, by running the OS connection tool (`netstat -ano` on Windows,
/// `ss -tnp` on Linux), parsing it, and resolving each PID to a process. This is
/// what turns "an IP is connected" into "which program is talking, and to
/// whom". Read-only and defensive — any failure yields an empty list.
/// </summary>
public sealed class ProcessConnectionReader
{
    private static readonly HashSet<string> LocalAddresses =
        new(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "::1", "0.0.0.0", "::", "*", "" };

    public IReadOnlyList<ProcessConnection> Read()
    {
        try
        {
            var raw = ReadRawConnections();
            var connections = new List<ProcessConnection>();
            var seen = new HashSet<string>();

            foreach (var row in raw)
            {
                if (!row.State.Equals("ESTABLISHED", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (row.RemotePort <= 0 || LocalAddresses.Contains(row.RemoteAddress) || IsPrivate(row.RemoteAddress))
                    continue;

                var key = $"{row.Pid}|{row.RemoteAddress}|{row.RemotePort}";
                if (!seen.Add(key))
                    continue;

                var (name, path) = ResolveProcess(row.Pid);
                var (trust, note) = ConnectionClassifier.Classify(name, path, row.RemoteAddress, row.RemotePort, row.State);
                connections.Add(new ProcessConnection
                {
                    Pid = row.Pid ?? 0,
                    Process = name,
                    ProcessPath = path,
                    RemoteAddress = row.RemoteAddress,
                    RemotePort = row.RemotePort,
                    State = row.State,
                    Trust = trust,
                    Note = note,
                });
            }

            return connections
                .OrderBy(c => c.Trust == ConnectionTrust.Suspicious ? 0 : c.Trust == ConnectionTrust.Unknown ? 1 : 2)
                .ThenBy(c => c.Process ?? "￿", StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<RawConnection> ReadRawConnections()
    {
        if (OperatingSystem.IsWindows())
            return NetstatParser.ParseWindows(RunCommand("netstat", "-ano"));
        if (OperatingSystem.IsLinux())
            return NetstatParser.ParseLinuxSs(RunCommand("ss", "-tnp"));
        return [];
    }

    private static string RunCommand(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
                return "";
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return output;
        }
        catch
        {
            return "";
        }
    }

    private static (string? Name, string? Path) ResolveProcess(int? pid)
    {
        if (pid is not int id || id <= 0)
            return (null, null);
        try
        {
            using var process = Process.GetProcessById(id);
            string? path = null;
            try { path = process.MainModule?.FileName; }
            catch { /* access denied on protected processes */ }
            return (process.ProcessName, path);
        }
        catch
        {
            return (null, null);
        }
    }

    // RFC1918 / link-local: LAN peers are not "phoning home" to the internet.
    private static bool IsPrivate(string address) =>
        address.StartsWith("10.", StringComparison.Ordinal) ||
        address.StartsWith("192.168.", StringComparison.Ordinal) ||
        address.StartsWith("169.254.", StringComparison.Ordinal) ||
        address.StartsWith("fe80:", StringComparison.OrdinalIgnoreCase) ||
        address.StartsWith("fd", StringComparison.OrdinalIgnoreCase) ||
        IsPrivate172(address);

    private static bool IsPrivate172(string address)
    {
        if (!address.StartsWith("172.", StringComparison.Ordinal))
            return false;
        var parts = address.Split('.');
        return parts.Length >= 2 && int.TryParse(parts[1], out var second) && second is >= 16 and <= 31;
    }
}
