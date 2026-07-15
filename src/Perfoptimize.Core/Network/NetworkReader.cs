using System.Net.NetworkInformation;

namespace Perfoptimize.Core.Network;

/// <summary>
/// Reads the machine's active TCP connections and listening ports through the
/// BCL's <see cref="IPGlobalProperties"/> — cross-platform (it reads the OS
/// tables on Windows and /proc/net on Linux) and free of native interop.
/// Read-only and never throws.
/// </summary>
public sealed class NetworkReader
{
    public NetworkSnapshot Read()
    {
        try
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();

            var connections = properties.GetActiveTcpConnections()
                .Select(c => new TcpConnectionInfo(
                    c.RemoteEndPoint.Address.ToString(), c.RemoteEndPoint.Port, c.State.ToString()))
                .ToList();

            var listeningPorts = properties.GetActiveTcpListeners()
                .Select(endpoint => endpoint.Port)
                .ToList();

            return NetworkAnalyzer.Analyze(connections, listeningPorts);
        }
        catch
        {
            // Some sandboxes forbid enumerating the TCP table; fail soft.
            return new NetworkSnapshot();
        }
    }
}
