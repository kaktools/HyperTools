using HyperTool.Models;
using Microsoft.Win32;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HyperTool.Services;

public sealed class HyperVSocketResourceMonitorHostListener : IDisposable
{
    private static readonly JsonSerializerOptions PacketJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Action<ResourceMonitorPacket> _onPacket;
    private readonly Guid _serviceId;
    private Socket? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;

    public HyperVSocketResourceMonitorHostListener(Action<ResourceMonitorPacket> onPacket, Guid? serviceId = null)
    {
        _onPacket = onPacket ?? throw new ArgumentNullException(nameof(onPacket));
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.ResourceMonitorServiceId;
    }

    public bool IsRunning { get; private set; }

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
        await using var stream = new NetworkStream(socket, ownsSocket: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 512, leaveOpen: false);

        string? line;
        try
        {
            line = await reader.ReadLineAsync(cancellationToken);
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        try
        {
            var packet = JsonSerializer.Deserialize<ResourceMonitorPacket>(line.Trim(), PacketJsonOptions);
            if (packet is not null)
            {
                _onPacket(packet);
            }
        }
        catch
        {
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
            serviceKey?.SetValue("ElementName", "HyperTool Hyper-V Socket Resource Monitor", RegistryValueKind.String);
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
