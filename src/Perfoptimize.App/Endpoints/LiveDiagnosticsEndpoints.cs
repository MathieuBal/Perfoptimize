using Perfoptimize.Core.Diagnostics;
using Perfoptimize.Core.Games;
using Perfoptimize.Core.Live;
using Perfoptimize.Core.Network;
using Perfoptimize.Core.Startup;

namespace Perfoptimize.App.Endpoints;

/// <summary>Whole-PC live view: continuous monitor, network attribution, and incident post-mortem.</summary>
internal static class LiveDiagnosticsEndpoints
{
    public static void MapLiveDiagnosticsEndpoints(this WebApplication app)
    {
        // Live: continuous whole-PC monitoring.
        app.MapGet("/api/live", async (LiveState state) =>
        {
            // Served from the background sampler's latest snapshot (instant); only if it
            // has not produced one yet do we sample inline as a fallback.
            var (snapshot, alerts, stableFps, fpsChange) = state.Read();
            if (snapshot is null)
            {
                var profiles = GameProfileRepository.LoadBuiltIn();
                var detected = new RunningGameDetector().DetectAny(profiles);
                snapshot = await new LiveMonitor().SampleAsync(
                    TimeSpan.FromMilliseconds(350), detected?.ProcessId, detected?.Name);
            }

            return Results.Json(new
            {
                timestamp = snapshot.Timestamp.ToString("o"),
                snapshot.SystemCpuPercent,
                snapshot.RamUsedPercent,
                snapshot.RamUsedBytes,
                snapshot.RamTotalBytes,
                snapshot.GpuPercent,
                snapshot.VramPercent,
                snapshot.CpuTemperatureC,
                snapshot.GpuTemperatureC,
                snapshot.GpuThrottling,
                snapshot.DpcPercent,
                snapshot.InterruptPercent,
                fps = stableFps ?? snapshot.Fps,
                snapshot.FpsOnePercentLow,
                fpsChange = fpsChange is null ? null : new { fpsChange.Before, fpsChange.After, fpsChange.DeltaPercent },
                snapshot.ProcessCount,
                game = snapshot.GameName,
                snapshot.GamePid,
                topByCpu = snapshot.TopByCpu.Select(u => new { u.Pid, u.Name, u.CpuPercent, u.MemoryBytes, u.MemoryPercent, u.IsGame }),
                topByMemory = snapshot.TopByMemory.Select(u => new { u.Pid, u.Name, u.CpuPercent, u.MemoryBytes, u.MemoryPercent, u.IsGame }),
                alerts = alerts.Select(a => new { a.Id, a.Kind, a.Level, a.Title, a.Detail, firstSeen = a.FirstSeen.ToString("o") }),
            });
        });

        // Network: what is connected and listening, attributed to the owning program.
        app.MapGet("/api/network", async () =>
        {
            var snapshot = new NetworkReader().Read();
            var connections = new ProcessConnectionReader().Read();
            var hosts = await HostResolver.ResolveAsync(
                connections.Select(c => c.RemoteAddress), TimeSpan.FromMilliseconds(1200));
            foreach (var connection in connections)
                connection.RemoteHost = hosts.GetValueOrDefault(connection.RemoteAddress);

            return Results.Json(new
            {
                snapshot.EstablishedCount,
                snapshot.ListeningCount,
                topRemotes = snapshot.TopRemotes.Select(r => new { r.Address, r.Connections }),
                listeningPorts = snapshot.ListeningPorts,
                connections = connections.Select(c => new
                {
                    c.Pid, c.Process, c.RemoteAddress, c.RemoteHost, c.RemotePort, c.State,
                    trust = c.Trust.ToString(), c.Note,
                }),
            });
        });

        // Diagnostics: explain recent freezes / crashes from the Windows event log.
        app.MapGet("/api/incidents", () =>
        {
            if (!OperatingSystem.IsWindows())
                return Results.Json(new { supported = false, incidents = Array.Empty<object>() });

            var records = new WindowsEventLogReader().ReadRecent(TimeSpan.FromDays(14));
            var incidents = IncidentClassifier.ClassifyAll(records).Take(60);
            return Results.Json(new
            {
                supported = true,
                incidents = incidents.Select(i => new
                {
                    kind = i.Kind.ToString(),
                    severity = i.Severity.ToString(),
                    when = i.When?.ToString("o"),
                    i.Process,
                    i.Title,
                    i.Detail,
                    i.LikelyCause,
                    i.SuggestedFix,
                }),
            });
        });

        // Startup: what auto-runs (logon + scheduled tasks), flagged against
        // processes seen "not responding"/crashing recently — the flashing-window
        // + freeze culprit usually lives here.
        app.MapGet("/api/startup", () =>
        {
            if (!OperatingSystem.IsWindows())
                return Results.Json(new { supported = false, entries = Array.Empty<object>(), hangs = Array.Empty<string>(), disabled = Array.Empty<object>() });

            var entries = new StartupScanner().Scan();

            var records = new WindowsEventLogReader().ReadRecent(TimeSpan.FromDays(14));
            var hangProcesses = IncidentClassifier.ClassifyAll(records)
                .Where(i => i.Kind is IncidentKind.AppHang or IncidentKind.AppCrash)
                .Select(i => i.Process)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!.ToLowerInvariant())
                .Distinct()
                .ToList();

            foreach (var entry in entries)
            {
                var command = entry.Command.ToLowerInvariant();
                entry.LinkedToHang = hangProcesses.Any(p => command.Contains(p));
            }

            var disabled = new StartupBackupStore(StartupBackupStore.DefaultDirectory()).List();

            return Results.Json(new
            {
                supported = true,
                entries = entries.Select(e => new
                {
                    e.Id, e.Name, e.Command, location = e.Location.ToString(), e.Detail, e.LinkedToHang,
                }),
                hangs = hangProcesses,
                disabled = disabled.Select(b => new
                {
                    b.Id, b.Name, location = b.Location.ToString(), method = b.Method.ToString(),
                    disabledAt = b.DisabledAt.ToString("o"),
                }),
            });
        });
    }
}
