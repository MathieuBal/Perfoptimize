using System.Diagnostics;
using System.Runtime.Versioning;

namespace Perfoptimize.Core.Monitoring.Frametimes;

/// <summary>
/// Drives Intel's PresentMon executable to capture real frame times during a
/// monitoring session (Windows only — PresentMon reads ETW presentation
/// events; nothing is injected into the game). The tool is looked up on PATH
/// and via the PERFOPTIMIZE_PRESENTMON environment variable.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PresentMonCapture
{
    private static readonly string[] CandidateNames = ["PresentMon", "PresentMon64", "presentmon"];

    /// <summary>Locates the PresentMon executable; null when not installed.</summary>
    public static string? FindExecutable()
    {
        if (Environment.GetEnvironmentVariable("PERFOPTIMIZE_PRESENTMON") is { Length: > 0 } configured &&
            File.Exists(configured))
            return configured;

        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var directory in pathDirectories)
        {
            foreach (var name in CandidateNames)
            {
                var candidate = Path.Combine(directory.Trim(), $"{name}.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Captures the given process for the given duration and returns the
    /// statistics, or null when PresentMon is unavailable or produced nothing
    /// (elevation refused, process not presenting, ...). Never throws: FPS
    /// capture is an enhancement, not a requirement.
    /// </summary>
    public static async Task<FrametimeStatistics?> CaptureAsync(
        int gameProcessId, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var executable = FindExecutable();
        if (executable is null)
            return null;

        var outputPath = Path.Combine(
            Path.GetTempPath(), $"perfoptimize-presentmon-{Guid.NewGuid():N}.csv");
        try
        {
            var seconds = Math.Max(1, (int)duration.TotalSeconds);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"--process_id {gameProcessId} --output_file \"{outputPath}\" " +
                            $"--timed {seconds} --terminate_after_timed --stop_existing_session",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
                return null;

            await process.WaitForExitAsync(cancellationToken);
            if (!File.Exists(outputPath))
                return null;

            var frametimes = PresentMonCsv.ReadFrametimes(await File.ReadAllTextAsync(
                outputPath, cancellationToken));
            return FrametimeAnalyzer.Analyze(frametimes);
        }
        catch
        {
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
            catch
            {
                // A leftover temp file is not worth failing over.
            }
        }
    }
}
