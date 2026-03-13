using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace HyperTool.Services;

/// <summary>
/// Host-side listener that keeps long-lived subscriber connections open and pushes
/// a <c>usb-share-changed</c> event to all connected guest subscribers whenever
/// <see cref="BroadcastAsync"/> is called.
/// </summary>
public sealed class HyperVSocketUsbChangeNotificationHostListener : IDisposable
{
    private static readonly byte[] UsbShareChangedPayload =
        Encoding.UTF8.GetBytes("{\"event\":\"usb-share-changed\"}\n");

    private sealed class SubscriberEntry
    {
        public Guid Id { get; } = Guid.NewGuid();
        public required Socket Socket { get; init; }
    }

    private readonly Guid _serviceId;
    private readonly ConcurrentDictionary<Guid, SubscriberEntry> _subscribers = new();
    private Socket? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;

    public HyperVSocketUsbChangeNotificationHostListener(Guid? serviceId = null)
    {
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.UsbChangeNotificationServiceId;
    }

    public bool IsRunning { get; private set; }

    public int SubscriberCount => _subscribers.Count;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        TryRegisterServiceGuid();

        var listener = new Socket((AddressFamily)34, SocketType.Stream, (ProtocolType)1);
        listener.Bind(new HyperVSocketEndPoint(HyperVSocketUsbTunnelDefaults.VmIdWildcard, _serviceId));
        listener.Listen(16);

        _listener = listener;
        _cts = new CancellationTokenSource();
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        IsRunning = true;
    }

    /// <summary>
    /// Sends <c>usb-share-changed</c> to all currently connected subscribers.
    /// Disconnected subscribers are pruned automatically.
    /// </summary>
    public async Task BroadcastAsync(CancellationToken cancellationToken = default)
    {
        if (_subscribers.IsEmpty)
        {
            return;
        }

        var toRemove = new List<Guid>();

        foreach (var (id, entry) in _subscribers)
        {
            try
            {
                await entry.Socket.SendAsync(UsbShareChangedPayload, SocketFlags.None, cancellationToken);
            }
            catch
            {
                toRemove.Add(id);
            }
        }

        foreach (var id in toRemove)
        {
            if (_subscribers.TryRemove(id, out var dead))
            {
                try { dead.Socket.Dispose(); } catch { }
            }
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket? socket = null;
            try
            {
                if (_listener is null)
                {
                    break;
                }

                socket = await _listener.AcceptAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(socket, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                socket?.Dispose();
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleClientAsync(Socket socket, CancellationToken cancellationToken)
    {
        var entry = new SubscriberEntry { Socket = socket };
        _subscribers[entry.Id] = entry;

        // The guest does not send any data on this channel. We just wait for the socket
        // to be closed (ReceiveAsync returning 0) so we can prune it from the subscriber list.
        var buffer = new byte[1];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var received = await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
                if (received == 0)
                {
                    break; // Graceful disconnect from guest.
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            _subscribers.TryRemove(entry.Id, out _);
            try { socket.Dispose(); } catch { }
        }
    }

    private void TryRegisterServiceGuid()
    {
        try
        {
            const string rootPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization\GuestCommunicationServices";
            using var rootKey = Registry.LocalMachine.CreateSubKey(rootPath, writable: true);
            if (rootKey is null)
            {
                return;
            }

            using var serviceKey = rootKey.CreateSubKey(_serviceId.ToString("D"), writable: true);
            serviceKey?.SetValue("ElementName", "HyperTool Hyper-V Socket USB Change Notification", RegistryValueKind.String);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;

        try
        {
            _cts?.Cancel();
        }
        catch
        {
        }

        try
        {
            _listener?.Dispose();
        }
        catch
        {
        }

        foreach (var (_, entry) in _subscribers)
        {
            try { entry.Socket.Dispose(); } catch { }
        }

        _subscribers.Clear();

        try
        {
            _acceptLoopTask?.Wait(TimeSpan.FromMilliseconds(250));
        }
        catch
        {
        }

        _cts?.Dispose();
        _cts = null;
        _listener = null;
        _acceptLoopTask = null;
    }
}
