namespace Perfoptimize.Core.Live;

/// <summary>A raw per-process reading taken at one instant: the CPU time
/// consumed so far and the working set. Two readings a moment apart give the
/// CPU usage over that interval.</summary>
public readonly record struct ProcessTimes(int Pid, string Name, TimeSpan CpuTime, long WorkingSetBytes);

/// <summary>A process' computed share of the machine over the sampling window.</summary>
public sealed class ProcessUsage
{
    public int Pid { get; init; }
    public string Name { get; init; } = "";
    public double CpuPercent { get; init; }
    public long MemoryBytes { get; init; }
    /// <summary>Working set as a share of physical RAM; null when total RAM is unknown.</summary>
    public double? MemoryPercent { get; init; }
    /// <summary>True when this process is the detected game itself.</summary>
    public bool IsGame { get; init; }
}

public enum LiveAlertKind { ProcessHog, CpuContention, MemoryPressure, Thermal, DriverLatency }

/// <summary>A live attribution: something is competing with the game right now.</summary>
public sealed class LiveAlert
{
    public LiveAlertKind Kind { get; init; }
    /// <summary>UI severity hint: "info", "warn" or "bad".</summary>
    public string Level { get; init; } = "warn";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
}

/// <summary>
/// A single instant of the whole machine: system CPU, RAM, the busiest
/// processes by CPU and by memory, and any contention the game is facing.
/// The UI polls this a few times a second to make it live.
/// </summary>
public sealed class LiveSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public double SystemCpuPercent { get; init; }
    public long? RamUsedBytes { get; init; }
    public long? RamTotalBytes { get; init; }
    public double? RamUsedPercent { get; init; }
    public double? GpuPercent { get; init; }
    public double? VramPercent { get; init; }
    public double? CpuTemperatureC { get; init; }
    public double? GpuTemperatureC { get; init; }
    /// <summary>True when the GPU is thermal-throttling (clocks cut to shed heat).</summary>
    public bool? GpuThrottling { get; init; }
    /// <summary>Share of CPU time in deferred procedure calls (driver latency proxy).</summary>
    public double? DpcPercent { get; init; }
    /// <summary>Share of CPU time servicing hardware interrupts.</summary>
    public double? InterruptPercent { get; init; }
    public int ProcessCount { get; init; }
    public string? GameName { get; init; }
    public int? GamePid { get; init; }
    public IReadOnlyList<ProcessUsage> TopByCpu { get; init; } = [];
    public IReadOnlyList<ProcessUsage> TopByMemory { get; init; } = [];
    public IReadOnlyList<LiveAlert> Alerts { get; init; } = [];
}
