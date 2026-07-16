using System.Collections.Concurrent;
using System.Net;

namespace Perfoptimize.Core.Network;

/// <summary>
/// Best-effort reverse DNS so the user sees "…steamcontent.com" instead of a bare
/// IP — the difference between "I recognise this" and "what is this?". Results
/// are cached across requests and the whole batch is bounded by a time budget so
/// a slow resolver never stalls the page. Unresolved addresses stay as null.
/// </summary>
public static class HostResolver
{
    private static readonly ConcurrentDictionary<string, string?> Cache = new();

    public static async Task<IReadOnlyDictionary<string, string?>> ResolveAsync(
        IEnumerable<string> addresses, TimeSpan budget)
    {
        var result = new Dictionary<string, string?>();
        var pending = new List<string>();

        foreach (var address in addresses.Distinct())
        {
            if (Cache.TryGetValue(address, out var cached))
                result[address] = cached;
            else
                pending.Add(address);
        }

        if (pending.Count == 0)
            return result;

        using var cts = new CancellationTokenSource(budget);
        var lookups = pending.Select(async address =>
        {
            try
            {
                var entry = await Dns.GetHostEntryAsync(address, cts.Token);
                var host = string.IsNullOrWhiteSpace(entry.HostName) ? null : entry.HostName;
                Cache[address] = host;
                return (address, host);
            }
            catch
            {
                Cache[address] = null;
                return (address, (string?)null);
            }
        });

        try
        {
            foreach (var (address, host) in await Task.WhenAll(lookups))
                result[address] = host;
        }
        catch
        {
            // Whatever resolved before the budget expired is already cached.
        }

        return result;
    }
}
