using System.Collections.Concurrent;

namespace HyperTool.Services;

public static class GuestNetworkDiagnosticsRegistry
{
    public sealed class GuestNetworkEntry
    {
        public string GuestComputerName { get; init; } = string.Empty;

        public string AdapterName { get; init; } = string.Empty;

        public string Ipv4Address { get; init; } = string.Empty;

        public string SubnetMask { get; init; } = string.Empty;

        public string Gateway { get; init; } = string.Empty;

        public DateTimeOffset LastSeenUtc { get; init; }
    }

    private static readonly ConcurrentDictionary<string, GuestNetworkEntry> EntriesByIpv4 = new(StringComparer.OrdinalIgnoreCase);

    public static void UpdateFromDiagnosticsAck(HyperVSocketDiagnosticsAck ack)
    {
        if (ack is null)
        {
            return;
        }

        if (ack.GuestIpv4Entries.Count > 0)
        {
            foreach (var entry in ack.GuestIpv4Entries)
            {
                var entryIpv4 = (entry.Ipv4Address ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(entryIpv4))
                {
                    continue;
                }

                EntriesByIpv4[entryIpv4] = new GuestNetworkEntry
                {
                    GuestComputerName = (ack.GuestComputerName ?? string.Empty).Trim(),
                    AdapterName = (entry.AdapterName ?? string.Empty).Trim(),
                    Ipv4Address = entryIpv4,
                    SubnetMask = (entry.SubnetMask ?? string.Empty).Trim(),
                    Gateway = (entry.Gateway ?? string.Empty).Trim(),
                    LastSeenUtc = DateTimeOffset.UtcNow
                };
            }

            return;
        }

        var ipv4 = (ack.GuestIpv4Address ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(ipv4))
        {
            return;
        }

        EntriesByIpv4[ipv4] = new GuestNetworkEntry
        {
            GuestComputerName = (ack.GuestComputerName ?? string.Empty).Trim(),
            AdapterName = (ack.GuestNetworkAdapterName ?? string.Empty).Trim(),
            Ipv4Address = ipv4,
            SubnetMask = (ack.GuestIpv4SubnetMask ?? string.Empty).Trim(),
            Gateway = (ack.GuestIpv4Gateway ?? string.Empty).Trim(),
            LastSeenUtc = DateTimeOffset.UtcNow
        };
    }

    public static bool TryGetFreshEntryByIpv4(string? ipv4Address, TimeSpan maxAge, out GuestNetworkEntry entry)
    {
        entry = new GuestNetworkEntry();
        if (string.IsNullOrWhiteSpace(ipv4Address))
        {
            return false;
        }

        if (!EntriesByIpv4.TryGetValue(ipv4Address.Trim(), out var candidate))
        {
            return false;
        }

        if ((DateTimeOffset.UtcNow - candidate.LastSeenUtc) > maxAge)
        {
            return false;
        }

        entry = candidate;
        return true;
    }

    public static bool TryGetSingleFreshEntry(TimeSpan maxAge, out GuestNetworkEntry entry)
    {
        entry = new GuestNetworkEntry();

        var now = DateTimeOffset.UtcNow;
        var freshEntries = EntriesByIpv4.Values
            .Where(candidate => (now - candidate.LastSeenUtc) <= maxAge)
            .OrderByDescending(candidate => candidate.LastSeenUtc)
            .ToList();

        if (freshEntries.Count != 1)
        {
            return false;
        }

        entry = freshEntries[0];
        return true;
    }
}