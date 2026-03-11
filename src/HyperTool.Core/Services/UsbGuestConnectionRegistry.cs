using System.Collections.Concurrent;

namespace HyperTool.Services;

public static class UsbGuestConnectionRegistry
{
    private sealed class GuestConnectionEntry
    {
        public string GuestComputerName { get; init; } = string.Empty;
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

        if (string.IsNullOrWhiteSpace(busId) && string.IsNullOrWhiteSpace(deviceKey))
        {
            return;
        }

        if (string.Equals(eventType, "usb-disconnected", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(busId))
            {
                ConnectedGuestsByBusId.TryRemove(busId, out _);
                DeviceKeyByBusId.TryRemove(busId, out _);
            }

            if (!string.IsNullOrWhiteSpace(deviceKey) && string.IsNullOrWhiteSpace(busId))
            {
                ConnectedGuestsByDeviceKey.TryRemove(deviceKey, out _);
            }

            return;
        }

        if ((string.Equals(eventType, "usb-connected", StringComparison.OrdinalIgnoreCase)
             || string.Equals(eventType, "usb-heartbeat", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(guestComputerName)
            && !string.IsNullOrWhiteSpace(deviceKey))
        {
            var entry = new GuestConnectionEntry
            {
                GuestComputerName = guestComputerName,
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

    private static string BuildDeviceIdentityKey(HyperVSocketDiagnosticsAck ack)
    {
        var hardwareId = NormalizeHardwareId(ack.HardwareId);
        if (!string.IsNullOrWhiteSpace(hardwareId))
        {
            return "hardware:" + hardwareId;
        }

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

        var busId = (ack.BusId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(busId))
        {
            return "busid:" + busId;
        }

        return string.Empty;
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
