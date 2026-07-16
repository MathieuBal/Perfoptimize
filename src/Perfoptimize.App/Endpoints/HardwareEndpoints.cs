using Perfoptimize.Core.Games;
using Perfoptimize.Core.Hardware;
using Perfoptimize.Core.Optimization;

namespace Perfoptimize.App.Endpoints;

/// <summary>Read-only diagnostics: hardware profile, detected/known games, PC advice.</summary>
internal static class HardwareEndpoints
{
    public static void MapHardwareEndpoints(this WebApplication app)
    {
        app.MapGet("/api/hardware", () =>
        {
            var profile = new HardwareScanner().Scan();
            return Results.Json(new
            {
                cpu = new { profile.Cpu.Name, profile.Cpu.PhysicalCores, profile.Cpu.LogicalCores, profile.Cpu.MaxClockMhz },
                gpus = profile.Gpus.Select(g => new { g.Name, g.Vendor, g.IsDiscrete, g.VramBytes, g.DriverVersion }),
                primaryGpu = profile.PrimaryGpu?.Name,
                memory = new { profile.Memory.TotalBytes, profile.Memory.AvailableBytes, profile.Memory.SpeedMtPerSecond },
                displays = profile.Displays.Select(d => new { d.Name, d.WidthPixels, d.HeightPixels, d.RefreshRateHz, d.IsPrimary }),
                storage = profile.StorageDevices.Select(s => new { s.Model, MediaType = s.MediaType.ToString(), s.TotalBytes }),
                os = new { profile.OperatingSystem.Name, profile.OperatingSystem.Version, profile.OperatingSystem.Architecture },
            });
        });

        app.MapGet("/api/games", () =>
        {
            var profiles = GameProfileRepository.LoadBuiltIn();
            var detected = new RunningGameDetector().DetectAny(profiles);
            return Results.Json(new
            {
                known = profiles.Select(p => new { p.Name, executables = p.Executables, settings = p.Settings.Count }),
                running = detected is null ? null : new
                {
                    detected.Name, detected.ProcessId, detected.IsKnown, detected.Reasons,
                },
            });
        });

        app.MapGet("/api/system", () =>
        {
            var profile = new HardwareScanner().Scan();
            var advice = SystemAdvisor.Analyze(profile);
            return Results.Json(new
            {
                advice = advice.Select(a => new { a.Category, a.Title, a.Detail, severity = a.Severity.ToString() }),
            });
        });
    }
}
