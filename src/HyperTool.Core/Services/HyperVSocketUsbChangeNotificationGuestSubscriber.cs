using System.Net.Sockets;
using System.Text;

namespace HyperTool.Services;

/// <summary>
/// Guest-side subscriber that connects to the host's USB change-notification service
/// and invokes a callback whenever a <c>usb-share-changed</c> event is received.
/// Returns (without throwing) when the connection is closed by the host or when
/// <paramref name="cancellationToken"/> is cancelled.
/// </summary>
public sealed class HyperVSocketUsbChangeNotificationGuestSubscriber
{
    private readonly Guid _serviceId;

    public HyperVSocketUsbChangeNotificationGuestSubscriber(Guid? serviceId = null)
    {
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.UsbChangeNotificationServiceId;
    }

    /// <summary>
    /// Connects to the host notification service and blocks until the connection drops
    /// or <paramref name="cancellationToken"/> is cancelled.  For each
    /// <c>usb-share-changed</c> event line received, <paramref name="onUsbShareChanged"/>
    /// is invoked synchronously before resuming the read loop.
    /// </summary>
    public async Task SubscribeAsync(Action onUsbShareChanged, CancellationToken cancellationToken)
    {
        using var socket = new Socket((AddressFamily)34, SocketType.Stream, (ProtocolType)1);

        cancellationToken.ThrowIfCancellationRequested();

        // Synchronous connect — consistent with other HyperV Socket guest clients.
        var endpoint = new HyperVSocketEndPoint(HyperVSocketUsbTunnelDefaults.VmIdParent, _serviceId);
        socket.Connect(endpoint);

        await using var stream = new NetworkStream(socket, ownsSocket: true);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 256,
            leaveOpen: false);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break; // EOF — host closed the connection.
            }

            if (line.Contains("usb-share-changed", StringComparison.OrdinalIgnoreCase))
            {
                onUsbShareChanged();
            }
        }
    }
}
