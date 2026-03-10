using HyperTool.Models;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HyperTool.Services;

public sealed class HyperVSocketFileGuestClient
{
    private readonly Guid _serviceId;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public HyperVSocketFileGuestClient(Guid? serviceId = null)
    {
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.FileServiceId;
    }

    public Task<HostFileServiceResponse> PingAsync(CancellationToken cancellationToken)
    {
        return SendAsync(new HostFileServiceRequest
        {
            Operation = "ping"
        }, cancellationToken);
    }

    public Task<HostFileServiceResponse> ListSharesAsync(CancellationToken cancellationToken)
    {
        return SendAsync(new HostFileServiceRequest
        {
            Operation = "list-shares"
        }, cancellationToken);
    }

    public Task<HostFileServiceResponse> SendAsync(HostFileServiceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            request.RequestId = Guid.NewGuid().ToString("N");
        }

        return SendCoreAsync(request, cancellationToken);
    }

    private async Task<HostFileServiceResponse> SendCoreAsync(HostFileServiceRequest request, CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromMilliseconds(6000));

        using var socket = new Socket((AddressFamily)34, SocketType.Stream, (ProtocolType)1);
        linkedCts.Token.ThrowIfCancellationRequested();
        var endpoint = new HyperVSocketEndPoint(HyperVSocketUsbTunnelDefaults.VmIdParent, _serviceId);
        ConnectWithRetry(socket, endpoint, linkedCts.Token);

        await using var stream = new NetworkStream(socket, ownsSocket: true);
        await using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 16 * 1024, leaveOpen: true)
        {
            NewLine = "\n"
        };
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 16 * 1024, leaveOpen: false);

        var payload = JsonSerializer.Serialize(request, SerializerOptions);
        await writer.WriteLineAsync(payload.AsMemory(), linkedCts.Token);
        await writer.FlushAsync(linkedCts.Token);

        var responsePayload = await reader.ReadLineAsync(linkedCts.Token);
        if (string.IsNullOrWhiteSpace(responsePayload))
        {
            throw new InvalidOperationException("Leere Antwort vom HyperTool File-Dienst.");
        }

        var response = JsonSerializer.Deserialize<HostFileServiceResponse>(responsePayload, SerializerOptions)
            ?? new HostFileServiceResponse();

        response.RequestId = string.IsNullOrWhiteSpace(response.RequestId)
            ? request.RequestId
            : response.RequestId;

        return response;
    }

    private static void ConnectWithRetry(Socket socket, EndPoint endpoint, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        var delays = new[]
        {
            TimeSpan.FromMilliseconds(75),
            TimeSpan.FromMilliseconds(220)
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
