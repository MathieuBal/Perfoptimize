using Perfoptimize.Core.Network;

namespace Perfoptimize.Core.Tests;

public class NetworkAnalyzerTests
{
    private static TcpConnectionInfo Conn(string addr, int port, string state = "Established") =>
        new(addr, port, state);

    [Fact]
    public void Groups_established_connections_by_remote_host_most_first()
    {
        var connections = new[]
        {
            Conn("140.82.121.4", 443),
            Conn("140.82.121.4", 443),
            Conn("104.16.0.1", 443),
            Conn("192.168.1.10", 27015, "Listen"), // not established -> ignored
        };

        var snap = NetworkAnalyzer.Analyze(connections, listeningPorts: []);

        Assert.Equal(3, snap.EstablishedCount);
        Assert.Equal("140.82.121.4", snap.TopRemotes[0].Address);
        Assert.Equal(2, snap.TopRemotes[0].Connections);
        Assert.Equal("104.16.0.1", snap.TopRemotes[1].Address);
    }

    [Fact]
    public void Drops_loopback_and_wildcard_from_the_remote_ranking()
    {
        var connections = new[]
        {
            Conn("127.0.0.1", 5000),
            Conn("::1", 5001),
            Conn("0.0.0.0", 0),
            Conn("8.8.8.8", 443),
        };

        var snap = NetworkAnalyzer.Analyze(connections, listeningPorts: []);

        // All four are "established", but only the real external peer is ranked.
        Assert.Equal(4, snap.EstablishedCount);
        Assert.Single(snap.TopRemotes);
        Assert.Equal("8.8.8.8", snap.TopRemotes[0].Address);
    }

    [Fact]
    public void Listening_ports_are_deduplicated_and_sorted()
    {
        var snap = NetworkAnalyzer.Analyze(
            connections: [], listeningPorts: [443, 80, 443, 0, 27015, 80]);

        Assert.Equal(new[] { 80, 443, 27015 }, snap.ListeningPorts);
        Assert.Equal(3, snap.ListeningCount);
    }

    [Fact]
    public void Empty_input_yields_an_empty_snapshot()
    {
        var snap = NetworkAnalyzer.Analyze(connections: [], listeningPorts: []);

        Assert.Equal(0, snap.EstablishedCount);
        Assert.Empty(snap.TopRemotes);
        Assert.Empty(snap.ListeningPorts);
    }
}
