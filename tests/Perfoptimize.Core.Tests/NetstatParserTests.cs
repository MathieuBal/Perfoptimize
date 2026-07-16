using Perfoptimize.Core.Network;

namespace Perfoptimize.Core.Tests;

public class NetstatParserTests
{
    [Fact]
    public void Parses_windows_netstat_ano_tcp_rows_with_pid()
    {
        const string output = """
            Active Connections

              Proto  Local Address          Foreign Address        State           PID
              TCP    192.168.1.10:50000     140.82.121.4:443       ESTABLISHED     8820
              TCP    0.0.0.0:445            0.0.0.0:0              LISTENING       4
              TCP    [2001:db8::1]:50001    [2606:4700::1111]:443  ESTABLISHED     9100
              UDP    0.0.0.0:5353           *:*                                    2000
            """;

        var rows = NetstatParser.ParseWindows(output);

        Assert.Equal(3, rows.Count); // TCP only, UDP dropped
        var established = rows[0];
        Assert.Equal("140.82.121.4", established.RemoteAddress);
        Assert.Equal(443, established.RemotePort);
        Assert.Equal("ESTABLISHED", established.State);
        Assert.Equal(8820, established.Pid);
        // IPv6 endpoint parsed with brackets stripped.
        Assert.Equal("2606:4700::1111", rows[2].RemoteAddress);
        Assert.Equal(9100, rows[2].Pid);
    }

    [Fact]
    public void Parses_linux_ss_tnp_rows_with_pid_from_process_field()
    {
        const string output = """
            State   Recv-Q  Send-Q  Local Address:Port    Peer Address:Port   Process
            ESTAB   0       0       192.168.1.10:50000    140.82.121.4:443    users:(("chrome",pid=8820,fd=120))
            LISTEN  0       128     0.0.0.0:445           0.0.0.0:*           users:(("smbd",pid=900,fd=30))
            """;

        var rows = NetstatParser.ParseLinuxSs(output);

        Assert.Equal(2, rows.Count);
        Assert.Equal("ESTABLISHED", rows[0].State); // ESTAB normalized
        Assert.Equal("140.82.121.4", rows[0].RemoteAddress);
        Assert.Equal(443, rows[0].RemotePort);
        Assert.Equal(8820, rows[0].Pid);
        Assert.Equal("LISTEN", rows[1].State);
        Assert.Equal(900, rows[1].Pid);
    }

    [Fact]
    public void Ignores_garbage_and_headers()
    {
        Assert.Empty(NetstatParser.ParseWindows("nonsense\nActive Connections\n  Proto Local\n"));
        Assert.Empty(NetstatParser.ParseLinuxSs("State Recv-Q Send-Q Local Peer\n"));
    }
}
