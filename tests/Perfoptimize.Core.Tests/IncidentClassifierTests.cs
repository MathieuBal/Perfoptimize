using Perfoptimize.Core.Diagnostics;

namespace Perfoptimize.Core.Tests;

public class IncidentClassifierTests
{
    private static IncidentRecord Record(
        string source, int code, string logFile = "Application",
        DateTimeOffset? when = null, params string[] insertionStrings) =>
        new()
        {
            Source = source,
            EventCode = code,
            LogFile = logFile,
            When = when,
            InsertionStrings = insertionStrings,
        };

    [Fact]
    public void Application_error_becomes_an_app_crash_with_the_process_name()
    {
        var incident = IncidentClassifier.Classify(
            Record("Application Error", 1000, insertionStrings:
                ["Cyberpunk2077.exe", "2.1", "0", "nvwgf2umx.dll", "1.0", "0", "0xc0000005"]));

        Assert.NotNull(incident);
        Assert.Equal(IncidentKind.AppCrash, incident.Kind);
        Assert.Equal("Cyberpunk2077.exe", incident.Process);
        Assert.Contains("nvwgf2umx.dll", incident.Detail);
        Assert.False(string.IsNullOrWhiteSpace(incident.SuggestedFix));
    }

    [Fact]
    public void Application_hang_becomes_an_app_hang()
    {
        var incident = IncidentClassifier.Classify(
            Record("Application Hang", 1002, insertionStrings: ["game.exe"]));

        Assert.NotNull(incident);
        Assert.Equal(IncidentKind.AppHang, incident.Kind);
        Assert.Equal("game.exe", incident.Process);
    }

    [Fact]
    public void Gpu_tdr_is_recognized_by_event_code_and_by_driver_source()
    {
        var byCode = IncidentClassifier.Classify(Record("Display", 4101, "System"));
        var byDriver = IncidentClassifier.Classify(Record("nvlddmkm", 14, "System"));

        Assert.Equal(IncidentKind.GpuReset, byCode?.Kind);
        Assert.Equal(IncidentKind.GpuReset, byDriver?.Kind);
        Assert.Equal(IncidentSeverity.Critical, byDriver!.Severity);
    }

    [Fact]
    public void Bugcheck_is_a_critical_bsod_carrying_the_stop_code()
    {
        var incident = IncidentClassifier.Classify(
            Record("BugCheck", 1001, "System", insertionStrings: ["0x00000133 (0x...)", "C:\\Windows\\MEMORY.DMP"]));

        Assert.NotNull(incident);
        Assert.Equal(IncidentKind.BugCheck, incident.Kind);
        Assert.Equal(IncidentSeverity.Critical, incident.Severity);
        Assert.Contains("0x00000133", incident.Detail);
    }

    [Fact]
    public void Kernel_power_41_is_an_unexpected_shutdown()
    {
        var incident = IncidentClassifier.Classify(
            Record("Microsoft-Windows-Kernel-Power", 41, "System"));

        Assert.NotNull(incident);
        Assert.Equal(IncidentKind.UnexpectedShutdown, incident.Kind);
    }

    [Fact]
    public void Resource_exhaustion_is_an_out_of_memory_incident()
    {
        var incident = IncidentClassifier.Classify(
            Record("Microsoft-Windows-Resource-Exhaustion-Detector", 2004, "System"));

        Assert.NotNull(incident);
        Assert.Equal(IncidentKind.OutOfMemory, incident.Kind);
    }

    [Fact]
    public void Disk_error_needs_both_a_disk_source_and_a_disk_code()
    {
        Assert.Equal(IncidentKind.DiskError,
            IncidentClassifier.Classify(Record("disk", 51, "System"))?.Kind);
        // A disk code from an unrelated source is not a disk error.
        Assert.Null(IncidentClassifier.Classify(Record("SomeApp", 51, "Application")));
    }

    [Fact]
    public void Unrecognized_events_are_dropped_and_results_are_newest_first()
    {
        var older = Record("Application Error", 1000, when: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            insertionStrings: ["a.exe"]);
        var newer = Record("Application Error", 1000, when: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            insertionStrings: ["b.exe"]);
        var noise = Record("Some-Info-Provider", 6005, "System");

        var incidents = IncidentClassifier.ClassifyAll([older, noise, newer]);

        Assert.Equal(2, incidents.Count); // noise dropped
        Assert.Equal("b.exe", incidents[0].Process); // newest first
        Assert.Equal("a.exe", incidents[1].Process);
    }
}
