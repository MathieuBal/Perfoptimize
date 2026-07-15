namespace Perfoptimize.Core.Network;

/// <summary>
/// Aggregates raw TCP data into a <see cref="NetworkSnapshot"/>: counts the
/// established connections, groups them by remote host, and lists the distinct
/// listening ports. Pure and unit-tested — the reader only supplies the raw
/// lists from the OS.
/// </summary>
public static class NetworkAnalyzer
{
    // Local/wildcard addresses are not "phoning home"; drop them from the
    // remote-host ranking so only real external peers show up.
    private static readonly HashSet<string> LocalAddresses =
        new(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "::1", "0.0.0.0", "::", "" };

    public static NetworkSnapshot Analyze(
        IReadOnlyList<TcpConnectionInfo> connections, IReadOnlyList<int> listeningPorts, int topN = 8)
    {
        var established = connections
            .Where(c => string.Equals(c.State, "Established", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var topRemotes = established
            .Where(c => c.RemotePort > 0 && !LocalAddresses.Contains(c.RemoteAddress))
            .GroupBy(c => c.RemoteAddress)
            .Select(g => new RemoteHost(g.Key, g.Count()))
            .OrderByDescending(r => r.Connections)
            .ThenBy(r => r.Address, StringComparer.Ordinal)
            .Take(topN)
            .ToList();

        var ports = listeningPorts
            .Where(p => p > 0)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        return new NetworkSnapshot
        {
            EstablishedCount = established.Count,
            ListeningCount = ports.Count,
            TopRemotes = topRemotes,
            ListeningPorts = ports,
        };
    }
}
