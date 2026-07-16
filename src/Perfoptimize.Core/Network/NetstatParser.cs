using System.Text.RegularExpressions;

namespace Perfoptimize.Core.Network;

/// <summary>
/// Parses the output of the OS connection tools into <see cref="RawConnection"/>
/// rows — the owning PID included, which the BCL's IPGlobalProperties cannot
/// give. Pure string handling, so it is unit-tested against captured output on
/// both platforms; the reader only supplies the text.
/// </summary>
public static partial class NetstatParser
{
    /// <summary>Parses Windows `netstat -ano` output (TCP rows only).</summary>
    public static IReadOnlyList<RawConnection> ParseWindows(string output)
    {
        var rows = new List<RawConnection>();
        foreach (var line in output.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            // Proto Local Foreign State PID
            if (parts.Length < 5 || !parts[0].Equals("TCP", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!int.TryParse(parts[4], out var pid))
                continue;

            var (localAddr, localPort) = SplitEndpoint(parts[1]);
            var (remoteAddr, remotePort) = SplitEndpoint(parts[2]);
            rows.Add(new RawConnection("TCP", localAddr, localPort, remoteAddr, remotePort, parts[3], pid));
        }
        return rows;
    }

    /// <summary>Parses Linux `ss -tnp` output.</summary>
    public static IReadOnlyList<RawConnection> ParseLinuxSs(string output)
    {
        var rows = new List<RawConnection>();
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("State", StringComparison.OrdinalIgnoreCase))
                continue;
            var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            // State Recv-Q Send-Q Local Peer [users:(("name",pid=NNN,fd=..))]
            if (parts.Length < 5)
                continue;

            var (localAddr, localPort) = SplitEndpoint(parts[3]);
            var (remoteAddr, remotePort) = SplitEndpoint(parts[4]);

            int? pid = null;
            if (parts.Length >= 6)
            {
                var match = PidPattern().Match(parts[5]);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var parsed))
                    pid = parsed;
            }

            var state = parts[0].Equals("ESTAB", StringComparison.OrdinalIgnoreCase) ? "ESTABLISHED" : parts[0];
            rows.Add(new RawConnection("TCP", localAddr, localPort, remoteAddr, remotePort, state, pid));
        }
        return rows;
    }

    /// <summary>Splits "addr:port", handling IPv6 forms like "[::1]:443" and "*:*".</summary>
    private static (string Address, int Port) SplitEndpoint(string endpoint)
    {
        var colon = endpoint.LastIndexOf(':');
        if (colon < 0)
            return (endpoint.Trim('[', ']'), 0);
        var address = endpoint[..colon].Trim('[', ']');
        return int.TryParse(endpoint[(colon + 1)..], out var port) ? (address, port) : (address, 0);
    }

    [GeneratedRegex(@"pid=(\d+)")]
    private static partial Regex PidPattern();
}
