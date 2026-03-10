using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using HyperTool.Models;

namespace HyperTool.Services;

public sealed class HyperVSocketHostIdentityGuestClient
{
    private readonly Guid _serviceId;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class HostIdentityPayload
    {
        public string HostName { get; set; } = string.Empty;
        public string Fqdn { get; set; } = string.Empty;
        public HostFeatureAvailability? Features { get; set; }
    }

    public HyperVSocketHostIdentityGuestClient(Guid? serviceId = null)
    {
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.HostIdentityServiceId;
    }

    public async Task<string?> FetchHostNameAsync(CancellationToken cancellationToken)
    {
        var identity = await FetchHostIdentityAsync(cancellationToken);
        if (identity is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(identity.HostName))
        {
            return identity.HostName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(identity.Fqdn))
        {
            return identity.Fqdn.Trim();
        }

        return null;
    }

    public async Task<HostIdentityInfo?> FetchHostIdentityAsync(CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromMilliseconds(2000));

        using var socket = new Socket((AddressFamily)34, SocketType.Stream, (ProtocolType)1);
        linkedCts.Token.ThrowIfCancellationRequested();
        var endpoint = new HyperVSocketEndPoint(HyperVSocketUsbTunnelDefaults.VmIdParent, _serviceId);
        ConnectWithRetry(socket, endpoint, linkedCts.Token);

        await using var stream = new NetworkStream(socket, ownsSocket: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);

        var payloadText = await reader.ReadLineAsync(linkedCts.Token);
        if (string.IsNullOrWhiteSpace(payloadText))
        {
            return null;
        }

        var payload = JsonSerializer.Deserialize<HostIdentityPayload>(payloadText, SerializerOptions);
        if (payload is null)
        {
            return null;
        }

        var hostName = payload.HostName?.Trim() ?? string.Empty;
        var fqdn = payload.Fqdn?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(hostName) && string.IsNullOrWhiteSpace(fqdn))
        {
            return null;
        }

        return new HostIdentityInfo
        {
            HostName = hostName,
            Fqdn = fqdn,
            Features = payload.Features ?? new HostFeatureAvailability()
        };
    }

    private static void ConnectWithRetry(Socket socket, EndPoint endpoint, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        var delays = new[]
        {
            TimeSpan.FromMilliseconds(70),
            TimeSpan.FromMilliseconds(210)
        };

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                socket.Connect(endpoint);
                return;
            }
            catch (SocketException ex) when (attempt < maxAttempts && IsTransientConnectSocketError(ex))
            {
                Task.Delay(delays[Math.Min(attempt - 1, delays.Length - 1)], cancellationToken).GetAwaiter().GetResult();
            }
        }
    }

    private static bool IsTransientConnectSocketError(SocketException ex)
    {
        return ex.SocketErrorCode is SocketError.NoBufferSpaceAvailable
            or SocketError.TryAgain
            or SocketError.TimedOut
            or SocketError.ConnectionRefused
            or SocketError.NetworkDown
            or SocketError.NetworkUnreachable
            or SocketError.HostDown
            or SocketError.HostUnreachable;
    }
}
