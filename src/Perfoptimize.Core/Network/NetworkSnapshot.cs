namespace Perfoptimize.Core.Network;

/// <summary>One active TCP connection, decoupled from the OS API so the
/// analyzer can be unit-tested.</summary>
public readonly record struct TcpConnectionInfo(string RemoteAddress, int RemotePort, string State);

/// <summary>A remote host the machine is talking to, with how many connections.</summary>
public sealed record RemoteHost(string Address, int Connections);

/// <summary>
/// A read-only inventory of the machine's TCP activity: how many connections
/// are established, which remote hosts they go to, and which ports are open for
/// listening. Enough to spot "what is phoning home" and an unexpected listener.
/// </summary>
public sealed class NetworkSnapshot
{
    public int EstablishedCount { get; init; }
    public int ListeningCount { get; init; }
    public IReadOnlyList<RemoteHost> TopRemotes { get; init; } = [];
    public IReadOnlyList<int> ListeningPorts { get; init; } = [];
}
