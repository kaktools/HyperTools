using Microsoft.Win32;
using Serilog;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace HyperTool.Services;

public sealed class HyperVSocketUsbHostTunnel : IDisposable
{
    private const int AcceptLoopRecoverThreshold = 6;
    private static readonly TimeSpan ExpectedRelayDisconnectLogInterval = TimeSpan.FromSeconds(30);
    private readonly Guid _serviceId;
    private readonly object _relayLogSync = new();
    private Socket? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;
    private DateTimeOffset _lastExpectedRelayDisconnectLogUtc = DateTimeOffset.MinValue;
    private int _suppressedExpectedRelayDisconnectCount;

    private readonly record struct RelayClientCorrelation(string SourceVmId, string GuestComputerName)
    {
        public string SourceVmIdForLog => string.IsNullOrWhiteSpace(SourceVmId) ? "unknown" : SourceVmId;
        public string GuestComputerNameForLog => string.IsNullOrWhiteSpace(GuestComputerName) ? "unknown" : GuestComputerName;
    }

    public HyperVSocketUsbHostTunnel(Guid? serviceId = null)
    {
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.ServiceId;
    }

    public bool IsRunning { get; private set; }

    public static bool IsServiceRegistered(Guid? serviceId = null)
    {
        var id = serviceId ?? HyperVSocketUsbTunnelDefaults.ServiceId;
        try
        {
            const string rootPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization\GuestCommunicationServices";
            using var rootKey = Registry.LocalMachine.OpenSubKey(rootPath, writable: false);
            if (rootKey is null)
            {
                return false;
            }

            using var serviceKey = rootKey.OpenSubKey(id.ToString("D"), writable: false);
            if (serviceKey is null)
            {
                return false;
            }

            var elementName = serviceKey.GetValue("ElementName") as string;
            return !string.IsNullOrWhiteSpace(elementName);
        }
        catch
        {
            return false;
        }
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        TryRegisterServiceGuid();

        var listener = CreateListener();

        _listener = listener;
        _cts = new CancellationTokenSource();
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        IsRunning = true;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        var consecutiveAcceptFailures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            Socket? hyperVClient = null;
            try
            {
                if (_listener is null)
                {
                    break;
                }

                hyperVClient = await _listener.AcceptAsync(cancellationToken);
                consecutiveAcceptFailures = 0;
                _ = Task.Run(() => HandleClientAsync(hyperVClient, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                hyperVClient?.Dispose();
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                consecutiveAcceptFailures++;
                if (consecutiveAcceptFailures == 1 || consecutiveAcceptFailures == AcceptLoopRecoverThreshold - 1)
                {
                    Log.Warning(
                        ex,
                        "Hyper-V USB host tunnel accept failed. Attempt={Attempt}/{Threshold}; ServiceId={ServiceId}",
                        consecutiveAcceptFailures,
                        AcceptLoopRecoverThreshold,
                        _serviceId.ToString("D"));
                }

                if (consecutiveAcceptFailures >= AcceptLoopRecoverThreshold)
                {
                    if (TryRecreateListener(ex))
                    {
                        consecutiveAcceptFailures = 0;
                    }
                }

                try
                {
                    await Task.Delay(250, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleClientAsync(Socket hyperVClient, CancellationToken cancellationToken)
    {
        var correlation = ResolveClientCorrelation(hyperVClient);

        try
        {
            await using var hyperVStream = new NetworkStream(hyperVClient, ownsSocket: true);
            using var tcpClient = await ConnectLocalUsbipWithRetryAsync(cancellationToken);
            await using var tcpStream = tcpClient.GetStream();

            var toTcpTask = hyperVStream.CopyToAsync(tcpStream, cancellationToken);
            var fromTcpTask = tcpStream.CopyToAsync(hyperVStream, cancellationToken);

            await Task.WhenAll(toTcpTask, fromTcpTask);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException ex) when (TryGetSocketException(ex, out var socketEx)
                                     && IsExpectedRelayDisconnectSocketError(socketEx.SocketErrorCode))
        {
            LogExpectedRelayDisconnectRateLimited(socketEx.SocketErrorCode, socketEx.NativeErrorCode, correlation);
        }
        catch (SocketException ex)
        {
            if (IsExpectedRelayDisconnectSocketError(ex.SocketErrorCode))
            {
                LogExpectedRelayDisconnectRateLimited(ex.SocketErrorCode, ex.NativeErrorCode, correlation);
                return;
            }

            Log.Warning(
                ex,
                "Hyper-V USB host tunnel client relay failed. SourceVmId={SourceVmId}; GuestComputerName={GuestComputerName}; SocketError={SocketError}; NativeError={NativeErrorCode}",
                correlation.SourceVmIdForLog,
                correlation.GuestComputerNameForLog,
                ex.SocketErrorCode,
                ex.NativeErrorCode);
        }
        catch (Exception ex)
        {
            Log.Debug(
                ex,
                "Hyper-V USB host tunnel client relay failed. SourceVmId={SourceVmId}; GuestComputerName={GuestComputerName}",
                correlation.SourceVmIdForLog,
                correlation.GuestComputerNameForLog);
        }
    }

    private void LogExpectedRelayDisconnectRateLimited(SocketError socketError, int nativeErrorCode, RelayClientCorrelation correlation)
    {
        var now = DateTimeOffset.UtcNow;

        lock (_relayLogSync)
        {
            if ((now - _lastExpectedRelayDisconnectLogUtc) >= ExpectedRelayDisconnectLogInterval)
            {
                var suppressed = _suppressedExpectedRelayDisconnectCount;
                _suppressedExpectedRelayDisconnectCount = 0;
                _lastExpectedRelayDisconnectLogUtc = now;

                if (suppressed > 0)
                {
                    Log.Debug(
                        "Hyper-V USB host tunnel expected relay disconnect (likely VM shutdown/restart). SourceVmId={SourceVmId}; GuestComputerName={GuestComputerName}; SocketError={SocketError}; NativeError={NativeErrorCode}; SuppressedDuplicates={SuppressedDuplicates}",
                        correlation.SourceVmIdForLog,
                        correlation.GuestComputerNameForLog,
                        socketError,
                        nativeErrorCode,
                        suppressed);
                }
                else
                {
                    Log.Debug(
                        "Hyper-V USB host tunnel expected relay disconnect (likely VM shutdown/restart). SourceVmId={SourceVmId}; GuestComputerName={GuestComputerName}; SocketError={SocketError}; NativeError={NativeErrorCode}",
                        correlation.SourceVmIdForLog,
                        correlation.GuestComputerNameForLog,
                        socketError,
                        nativeErrorCode);
                }

                return;
            }

            _suppressedExpectedRelayDisconnectCount++;
        }
    }

    private static bool TryGetSocketException(Exception ex, out SocketException socketException)
    {
        for (var current = ex; current is not null; current = current.InnerException!)
        {
            if (current is SocketException currentSocket)
            {
                socketException = currentSocket;
                return true;
            }

            if (current.InnerException is null)
            {
                break;
            }
        }

        socketException = null!;
        return false;
    }

    private static bool IsExpectedRelayDisconnectSocketError(SocketError socketError)
    {
        return socketError is SocketError.ConnectionAborted
            or SocketError.ConnectionReset
            or SocketError.OperationAborted
            or SocketError.Shutdown;
    }

    private static RelayClientCorrelation ResolveClientCorrelation(Socket socket)
    {
        var sourceVmId = TryGetRemoteVmId(socket);
        var guestComputerName = string.Empty;

        if (!string.IsNullOrWhiteSpace(sourceVmId))
        {
            UsbGuestConnectionRegistry.TryGetGuestComputerNameBySourceVmId(
                sourceVmId,
                out guestComputerName,
                maxAge: TimeSpan.FromMinutes(10));
        }

        return new RelayClientCorrelation(sourceVmId, guestComputerName);
    }

    private static string TryGetRemoteVmId(Socket socket)
    {
        try
        {
            if (socket.RemoteEndPoint is HyperVSocketEndPoint hyperVSocketEndPoint)
            {
                return hyperVSocketEndPoint.VmId.ToString("D");
            }

            if (socket.RemoteEndPoint is EndPoint remoteEndPoint)
            {
                var parser = new HyperVSocketEndPoint(Guid.Empty, Guid.Empty);
                if (parser.Create(remoteEndPoint.Serialize()) is HyperVSocketEndPoint parsed)
                {
                    return parsed.VmId.ToString("D");
                }
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static async Task<TcpClient> ConnectLocalUsbipWithRetryAsync(CancellationToken cancellationToken)
    {
        var retryDelaysMs = new[] { 120, 320, 700 };
        Exception? lastError = null;

        for (var attempt = 0; attempt <= retryDelaysMs.Length; attempt++)
        {
            var tcpClient = new TcpClient();
            try
            {
                await tcpClient.ConnectAsync(IPAddress.Loopback, HyperVSocketUsbTunnelDefaults.UsbIpTcpPort, cancellationToken);
                return tcpClient;
            }
            catch (Exception ex)
            {
                lastError = ex;
                tcpClient.Dispose();

                if (attempt >= retryDelaysMs.Length || !IsTransientLocalUsbipConnectFailure(ex))
                {
                    break;
                }

                await Task.Delay(retryDelaysMs[attempt], cancellationToken);
            }
        }

        throw lastError ?? new InvalidOperationException("Lokale usbip-Verbindung konnte nicht hergestellt werden.");
    }

    private static bool IsTransientLocalUsbipConnectFailure(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return false;
        }

        if (ex is SocketException socketEx)
        {
            return socketEx.SocketErrorCode is SocketError.ConnectionRefused
                or SocketError.ConnectionReset
                or SocketError.TimedOut
                or SocketError.TryAgain
                or SocketError.NetworkDown
                or SocketError.NetworkReset
                or SocketError.NetworkUnreachable
                or SocketError.HostDown
                or SocketError.HostUnreachable;
        }

        return ex is InvalidOperationException;
    }

    private Socket CreateListener()
    {
        var listener = new Socket((AddressFamily)34, SocketType.Stream, (ProtocolType)1);
        listener.Bind(new HyperVSocketEndPoint(HyperVSocketUsbTunnelDefaults.VmIdWildcard, _serviceId));
        listener.Listen(64);
        return listener;
    }

    private bool TryRecreateListener(Exception triggerException)
    {
        try
        {
            _listener?.Dispose();
        }
        catch
        {
        }

        try
        {
            _listener = CreateListener();
            Log.Warning(
                triggerException,
                "Hyper-V USB host tunnel listener was recreated after repeated accept failures. ServiceId={ServiceId}",
                _serviceId.ToString("D"));
            return true;
        }
        catch (Exception recreateEx)
        {
            Log.Warning(
                recreateEx,
                "Hyper-V USB host tunnel listener recreation failed. ServiceId={ServiceId}",
                _serviceId.ToString("D"));
            return false;
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
            serviceKey?.SetValue("ElementName", "HyperTool Hyper-V Socket USB Tunnel", RegistryValueKind.String);
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