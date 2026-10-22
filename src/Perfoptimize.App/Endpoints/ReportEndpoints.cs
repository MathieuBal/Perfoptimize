using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Perfoptimize.Core.Hardware;
using Perfoptimize.Core.Storage;

namespace Perfoptimize.App.Endpoints;

/// <summary>Exports a support-friendly snapshot without usernames or absolute paths.</summary>
internal static class ReportEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void MapReportEndpoints(this WebApplication app)
    {
        app.MapGet("/api/export-report", (long? sessionId) =>
        {
            if (sessionId is null or <= 0)
                return Results.BadRequest(new { ok = false, message = "Session d'analyse manquante." });

            SessionRecord? session;
            try
            {
                using var store = new SessionStore(SessionStore.DefaultDatabasePath());
                session = store.GetById(sessionId.Value);
            }
            catch
            {
                return Results.Problem("Impossible de lire l'historique local.", statusCode: 500);
            }

            if (session is null)
                return Results.NotFound(new { ok = false, message = "Session introuvable." });

            var hardware = new HardwareScanner().Scan();
            var report = new
            {
                schemaVersion = 1,
                generatedAtUtc = DateTimeOffset.UtcNow,
                perfoptimize = new
                {
                    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev",
                    channel = "alpha",
                },
                privacy = new
                {
                    containsUsername = false,
                    containsAbsoluteConfigPaths = false,
                    note = "Rapport technique local destiné au diagnostic d'une version alpha.",
                },
                system = new
                {
                    os = new
                    {
                        hardware.OperatingSystem.Name,
                        hardware.OperatingSystem.Version,
                        hardware.OperatingSystem.Architecture,
                    },
                    cpu = new
                    {
                        hardware.Cpu.Name,
                        hardware.Cpu.PhysicalCores,
                        hardware.Cpu.LogicalCores,
                        hardware.Cpu.MaxClockMhz,
                    },
                    gpus = hardware.Gpus.Select(g => new
                    {
                        g.Name,
                        g.Vendor,
                        g.IsDiscrete,
                        g.VramBytes,
                        g.DriverVersion,
                    }),
                    memory = new
                    {
                        hardware.Memory.TotalBytes,
                        hardware.Memory.SpeedMtPerSecond,
                    },
                    displays = hardware.Displays.Select(d => new
                    {
                        d.WidthPixels,
                        d.HeightPixels,
                        d.RefreshRateHz,
                        d.IsPrimary,
                    }),
                    storage = hardware.StorageDevices.Select(s => new
                    {
                        mediaType = s.MediaType.ToString(),
                        s.TotalBytes,
                    }),
                },
                session = new
                {
                    session.Id,
                    session.StartedAt,
                    session.GameName,
                    bottleneck = session.Bottleneck.ToString(),
                    session.TargetFps,
                    session.AverageFps,
                    session.OnePercentLowFps,
                    session.Summary,
                    plan = session.Plan.Select(p => new
                    {
                        p.Title,
                        p.Reason,
                        p.SettingName,
                        p.ExpectedGainPercent,
                        p.ConfigFile,
                        p.ConfigSection,
                        p.ConfigKey,
                        p.ObservedValue,
                        p.ProposedValue,
                    }),
                },
            };

            var payload = JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
            var safeGame = SanitizeFileName(session.GameName ?? "unknown-game");
            var filename = $"perfoptimize-alpha-{safeGame}-session-{session.Id}.json";
            return Results.File(payload, "application/json; charset=utf-8", filename);
        });
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : char.ToLowerInvariant(c))
            .ToArray());
        while (sanitized.Contains("--", StringComparison.Ordinal))
            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);
        return sanitized.Trim('-');
    }
}
