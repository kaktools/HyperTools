using HyperTool.Models;
using System.Collections.Concurrent;

namespace HyperTool.Services;

public static class UsbGuestConnectionRegistry
{
    private sealed class GuestConnectionEntry
    {
        public string GuestComputerName { get; init; } = string.Empty;
        public string SourceVmId { get; init; } = string.Empty;
        public DateTimeOffset LastSeenUtc { get; init; }
    }

    private static readonly ConcurrentDictionary<string, GuestConnectionEntry> ConnectedGuestsByDeviceKey = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, GuestConnectionEntry> ConnectedGuestsByBusId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> DeviceKeyByBusId = new(StringComparer.OrdinalIgnoreCase);

    public static void UpdateFromDiagnosticsAck(HyperVSocketDiagnosticsAck ack)
    {
        if (ack is null)
        {
            return;
        }

        var busId = (ack.BusId ?? string.Empty).Trim();
        var deviceKey = BuildDeviceIdentityKey(ack);
        var eventType = (ack.EventType ?? string.Empty).Trim();
        var guestComputerName = (ack.GuestComputerName ?? string.Empty).Trim();
        var sourceVmId = (ack.SourceVmId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(busId) && string.IsNullOrWhiteSpace(deviceKey))
        {
            return;
        }

        if (string.Equals(eventType, "usb-disconnected", StringComparison.OrdinalIgnoreCase))
        {
            var skipDeviceKeyRemoval = false;
            var skipBusIdRemoval = false;

            if (!string.IsNullOrWhiteSpace(sourceVmId))
            {
                if (!string.IsNullOrWhiteSpace(deviceKey)
                    && ConnectedGuestsByDeviceKey.TryGetValue(deviceKey, out var existingByDeviceKey)
                    && !string.IsNullOrWhiteSpace(existingByDeviceKey.SourceVmId)
                    && !string.Equals(existingByDeviceKey.SourceVmId, sourceVmId, StringComparison.OrdinalIgnoreCase))
                {
                    skipDeviceKeyRemoval = true;
                }

                if (!string.IsNullOrWhiteSpace(busId)
                    && ConnectedGuestsByBusId.TryGetValue(busId, out var existingByBusId)
                    && !string.IsNullOrWhiteSpace(existingByBusId.SourceVmId)
                    && !string.Equals(existingByBusId.SourceVmId, sourceVmId, StringComparison.OrdinalIgnoreCase))
                {
                    skipBusIdRemoval = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(deviceKey))
            {
                if (!skipDeviceKeyRemoval)
                {
                    ConnectedGuestsByDeviceKey.TryRemove(deviceKey, out _);
                }
            }

            if (!string.IsNullOrWhiteSpace(busId))
            {
                if (!skipBusIdRemoval)
                {
                    ConnectedGuestsByBusId.TryRemove(busId, out _);
                    DeviceKeyByBusId.TryRemove(busId, out _);
                }
            }

            return;
        }

        if ((string.Equals(eventType, "usb-connected", StringComparison.OrdinalIgnoreCase)
             || string.Equals(eventType, "usb-heartbeat", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(deviceKey)
            && (!string.IsNullOrWhiteSpace(guestComputerName) || !string.IsNullOrWhiteSpace(sourceVmId)))
        {
            GuestConnectionEntry? existingEntry = null;
            if (!ConnectedGuestsByDeviceKey.TryGetValue(deviceKey, out existingEntry)
                && !string.IsNullOrWhiteSpace(busId))
            {
                ConnectedGuestsByBusId.TryGetValue(busId, out existingEntry);
            }

            var entry = new GuestConnectionEntry
            {
                GuestComputerName = !string.IsNullOrWhiteSpace(guestComputerName)
                    ? guestComputerName
                    : (existingEntry?.GuestComputerName ?? string.Empty),
                SourceVmId = !string.IsNullOrWhiteSpace(sourceVmId)
                    ? sourceVmId
                    : (existingEntry?.SourceVmId ?? string.Empty),
                LastSeenUtc = DateTimeOffset.UtcNow
            };

            ConnectedGuestsByDeviceKey[deviceKey] = entry;

            if (!string.IsNullOrWhiteSpace(busId))
            {
                DeviceKeyByBusId[busId] = deviceKey;
                ConnectedGuestsByBusId[busId] = entry;
            }
        }
    }

    public static bool TryGetGuestComputerName(string? busId, out string guestComputerName)
    {
        guestComputerName = string.Empty;

        if (string.IsNullOrWhiteSpace(busId))
        {
            return false;
        }

        var normalizedBusId = busId.Trim();

        if (ConnectedGuestsByBusId.TryGetValue(normalizedBusId, out var busEntry))
        {
            guestComputerName = busEntry.GuestComputerName;
            return true;
        }

        if (!DeviceKeyByBusId.TryGetValue(normalizedBusId, out var deviceKey)
            || string.IsNullOrWhiteSpace(deviceKey))
        {
            deviceKey = "busid:" + normalizedBusId;
        }

        if (!ConnectedGuestsByDeviceKey.TryGetValue(deviceKey, out var entry))
        {
            return false;
        }

        guestComputerName = entry.GuestComputerName;
        return true;
    }

    public static bool TryGetGuestVmId(string? busId, out string sourceVmId)
    {
        sourceVmId = string.Empty;

        if (string.IsNullOrWhiteSpace(busId))
        {
            return false;
        }

        var normalizedBusId = busId.Trim();

        if (ConnectedGuestsByBusId.TryGetValue(normalizedBusId, out var busEntry)
            && !string.IsNullOrWhiteSpace(busEntry.SourceVmId))
        {
            sourceVmId = busEntry.SourceVmId;
            return true;
        }

        if (!DeviceKeyByBusId.TryGetValue(normalizedBusId, out var deviceKey)
            || string.IsNullOrWhiteSpace(deviceKey))
        {
            deviceKey = "busid:" + normalizedBusId;
        }

        if (!ConnectedGuestsByDeviceKey.TryGetValue(deviceKey, out var entry)
            || string.IsNullOrWhiteSpace(entry.SourceVmId))
        {
            return false;
        }

        sourceVmId = entry.SourceVmId;
        return true;
    }

    public static bool TryGetGuestComputerNameBySourceVmId(string? sourceVmId, out string guestComputerName, TimeSpan? maxAge = null)
    {
        guestComputerName = string.Empty;

        var normalizedVmId = (sourceVmId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedVmId))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        GuestConnectionEntry? newestMatch = null;

        foreach (var entry in ConnectedGuestsByDeviceKey.Values)
        {
            if (!string.Equals(entry.SourceVmId, normalizedVmId, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(entry.GuestComputerName))
            {
                continue;
            }

            if (maxAge.HasValue && (now - entry.LastSeenUtc) > maxAge.Value)
            {
                continue;
            }

            if (newestMatch is null || entry.LastSeenUtc > newestMatch.LastSeenUtc)
            {
                newestMatch = entry;
            }
        }

        if (newestMatch is null)
        {
            return false;
        }

        guestComputerName = newestMatch.GuestComputerName;
        return true;
    }

    public static bool TryGetFreshGuestComputerName(string? busId, TimeSpan maxAge, out string guestComputerName)
    {
        guestComputerName = string.Empty;

        if (string.IsNullOrWhiteSpace(busId))
        {
            return false;
        }

        var normalizedBusId = busId.Trim();

        if (ConnectedGuestsByBusId.TryGetValue(normalizedBusId, out var busEntry))
        {
            if ((DateTimeOffset.UtcNow - busEntry.LastSeenUtc) > maxAge)
            {
                return false;
            }

            guestComputerName = busEntry.GuestComputerName;
            return true;
        }

        if (!DeviceKeyByBusId.TryGetValue(normalizedBusId, out var deviceKey)
            || string.IsNullOrWhiteSpace(deviceKey))
        {
            deviceKey = "busid:" + normalizedBusId;
        }

        if (!ConnectedGuestsByDeviceKey.TryGetValue(deviceKey, out var entry))
        {
            return false;
        }

        if ((DateTimeOffset.UtcNow - entry.LastSeenUtc) > maxAge)
        {
            return false;
        }

        guestComputerName = entry.GuestComputerName;
        return true;
    }

    public static bool TryGetFreshGuestComputerName(UsbIpDeviceInfo? device, TimeSpan maxAge, out string guestComputerName)
    {
        guestComputerName = string.Empty;

        if (device is null)
        {
            return false;
        }

        if (TryGetFreshGuestComputerName(device.BusId, maxAge, out guestComputerName))
        {
            return true;
        }

        foreach (var key in BuildDeviceIdentityAliasKeys(device))
        {
            if (!ConnectedGuestsByDeviceKey.TryGetValue(key, out var entry))
            {
                continue;
            }

            if ((DateTimeOffset.UtcNow - entry.LastSeenUtc) > maxAge)
            {
                continue;
            }

            guestComputerName = entry.GuestComputerName;

            var busId = (device.BusId ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(busId))
            {
                DeviceKeyByBusId[busId] = key;
                ConnectedGuestsByBusId[busId] = entry;
            }

            return true;
        }

        return false;
    }

    public static bool TryGetFreshGuestVmId(string? busId, TimeSpan maxAge, out string sourceVmId)
    {
        sourceVmId = string.Empty;

        if (string.IsNullOrWhiteSpace(busId))
        {
            return false;
        }

        var normalizedBusId = busId.Trim();

        if (ConnectedGuestsByBusId.TryGetValue(normalizedBusId, out var busEntry))
        {
            if ((DateTimeOffset.UtcNow - busEntry.LastSeenUtc) > maxAge || string.IsNullOrWhiteSpace(busEntry.SourceVmId))
            {
                return false;
            }

            sourceVmId = busEntry.SourceVmId;
            return true;
        }

        if (!DeviceKeyByBusId.TryGetValue(normalizedBusId, out var deviceKey)
            || string.IsNullOrWhiteSpace(deviceKey))
        {
            deviceKey = "busid:" + normalizedBusId;
        }

        if (!ConnectedGuestsByDeviceKey.TryGetValue(deviceKey, out var entry)
            || (DateTimeOffset.UtcNow - entry.LastSeenUtc) > maxAge
            || string.IsNullOrWhiteSpace(entry.SourceVmId))
        {
            return false;
        }

        sourceVmId = entry.SourceVmId;
        return true;
    }

    public static bool TryGetFreshGuestVmId(UsbIpDeviceInfo? device, TimeSpan maxAge, out string sourceVmId)
    {
        sourceVmId = string.Empty;

        if (device is null)
        {
            return false;
        }

        if (TryGetFreshGuestVmId(device.BusId, maxAge, out sourceVmId))
        {
            return true;
        }

        foreach (var key in BuildDeviceIdentityAliasKeys(device))
        {
            if (!ConnectedGuestsByDeviceKey.TryGetValue(key, out var entry))
            {
                continue;
            }

            if ((DateTimeOffset.UtcNow - entry.LastSeenUtc) > maxAge || string.IsNullOrWhiteSpace(entry.SourceVmId))
            {
                continue;
            }

            sourceVmId = entry.SourceVmId;

            var busId = (device.BusId ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(busId))
            {
                DeviceKeyByBusId[busId] = key;
                ConnectedGuestsByBusId[busId] = entry;
            }

            return true;
        }

        return false;
    }

    public static bool HasAnyFreshGuestConnection(TimeSpan maxAge)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var (_, entry) in ConnectedGuestsByDeviceKey)
        {
            if ((now - entry.LastSeenUtc) <= maxAge)
            {
                return true;
            }
        }

        foreach (var (_, entry) in ConnectedGuestsByBusId)
        {
            if ((now - entry.LastSeenUtc) <= maxAge)
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildDeviceIdentityKey(HyperVSocketDiagnosticsAck ack)
    {
        var persistedGuid = (ack.PersistedGuid ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(persistedGuid))
        {
            return "guid:" + persistedGuid;
        }

        var instanceId = (ack.InstanceId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            return "instance:" + instanceId;
        }

        var hardwareId = NormalizeHardwareId(ack.HardwareId);
        if (!string.IsNullOrWhiteSpace(hardwareId))
        {
            return "hardware:" + hardwareId;
        }

        var busId = (ack.BusId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(busId))
        {
            return "busid:" + busId;
        }

        return string.Empty;
    }

    private static IEnumerable<string> BuildDeviceIdentityAliasKeys(UsbIpDeviceInfo device)
    {
        var persistedGuid = (device.PersistedGuid ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(persistedGuid))
        {
            yield return "guid:" + persistedGuid;
        }

        var instanceId = (device.InstanceId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            yield return "instance:" + instanceId;
        }

        var hardwareId = NormalizeHardwareId(device.HardwareIdentityKey);
        if (string.IsNullOrWhiteSpace(hardwareId))
        {
            hardwareId = NormalizeHardwareId(device.HardwareId);
        }

        if (!string.IsNullOrWhiteSpace(hardwareId))
        {
            yield return "hardware:" + hardwareId;
        }

        var busId = (device.BusId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(busId))
        {
            yield return "busid:" + busId;
        }
    }

    private static string NormalizeHardwareId(string? hardwareId)
    {
        if (string.IsNullOrWhiteSpace(hardwareId))
        {
            return string.Empty;
        }

        return hardwareId.Trim().ToUpperInvariant();
    }
}
