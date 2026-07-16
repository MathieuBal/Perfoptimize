namespace Perfoptimize.Core.Network;

/// <summary>A raw connection row parsed from netstat/ss, before enrichment.</summary>
public readonly record struct RawConnection(
    string Protocol, string LocalAddress, int LocalPort,
    string RemoteAddress, int RemotePort, string State, int? Pid);

/// <summary>
/// How confidently a connection can be judged. Deliberately conservative — the
/// point is to help the user *understand* what is talking to the network, not to
/// cry "malware": most connections are simply identified programs.
/// </summary>
public enum ConnectionTrust
{
    /// <summary>Owned by an identified process on a common port.</summary>
    Normal,
    /// <summary>The owning process could not be identified — worth a look if unexpected.</summary>
    Unknown,
    /// <summary>A strong signal worth checking (e.g. the exe lives in a temp folder).</summary>
    Suspicious,
}

/// <summary>An outbound connection attributed to the program that owns it.</summary>
public sealed class ProcessConnection
{
    public int Pid { get; init; }
    public string? Process { get; init; }
    public string? ProcessPath { get; init; }
    public string RemoteAddress { get; init; } = "";
    /// <summary>Reverse-DNS name of the remote, when it resolves (e.g. "server-1-2-3.steamcontent.com").</summary>
    public string? RemoteHost { get; set; }
    public int RemotePort { get; init; }
    public string State { get; init; } = "";
    public ConnectionTrust Trust { get; init; }
    public string? Note { get; init; }
}
