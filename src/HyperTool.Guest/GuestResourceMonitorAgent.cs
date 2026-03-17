using HyperTool.Models;
using HyperTool.Services;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HyperTool.Guest;

internal sealed class GuestResourceMonitorAgent : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SystemResourceSampler _sampler = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private int _intervalMs = 1000;

    public bool IsRunning { get; private set; }

    public void Start(int intervalMs)
    {
        Stop();

        _intervalMs = NormalizeInterval(intervalMs);
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
        IsRunning = true;
    }

    public void UpdateInterval(int intervalMs)
    {
        _intervalMs = NormalizeInterval(intervalMs);
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var reconnectDelayUntilUtc = DateTimeOffset.MinValue;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                if (now < reconnectDelayUntilUtc)
                {
                    var waitMs = (int)Math.Clamp((reconnectDelayUntilUtc - now).TotalMilliseconds, 50, 5000);
                    await Task.Delay(waitMs, cancellationToken);
                    continue;
                }

                var (cpu, ramUsed, ramTotal) = _sampler.Sample();
                var packet = new ResourceMonitorPacket
                {
                    Vm = Environment.MachineName,
                    Cpu = Math.Round(cpu, 1),
                    RamUsed = Math.Round(ramUsed, 2),
                    RamTotal = Math.Round(ramTotal, 2),
                    SentAtUtc = DateTime.UtcNow.ToString("O")
                };

                await SendPacketAsync(packet, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                reconnectDelayUntilUtc = DateTimeOffset.UtcNow.AddSeconds(5);
            }

            try
            {
                await Task.Delay(_intervalMs, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static async Task SendPacketAsync(ResourceMonitorPacket packet, CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromMilliseconds(1200));

        using var gateLease = await HyperVSocketClientConcurrencyGate.AcquireAsync(linkedCts.Token);
        using var socket = new Socket((AddressFamily)34, SocketType.Stream, (ProtocolType)1);
        linkedCts.Token.ThrowIfCancellationRequested();
        socket.Connect(new HyperVSocketEndPoint(HyperVSocketUsbTunnelDefaults.VmIdParent, HyperVSocketUsbTunnelDefaults.ResourceMonitorServiceId));

        await using var stream = new NetworkStream(socket, ownsSocket: true);
        await using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 256, leaveOpen: false)
        {
            NewLine = "\n"
        };

        var payload = JsonSerializer.Serialize(packet, SerializerOptions);
        await writer.WriteLineAsync(payload.AsMemory(), linkedCts.Token);
        await writer.FlushAsync(linkedCts.Token);
    }

    private static int NormalizeInterval(int intervalMs)
    {
        return intervalMs switch
        {
            500 => 500,
            1000 => 1000,
            2000 => 2000,
            5000 => 5000,
            _ => 1000
        };
    }

    public void Stop()
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
            _loopTask?.Wait(TimeSpan.FromMilliseconds(250));
        }
        catch
        {
        }

        _cts?.Dispose();
        _cts = null;
        _loopTask = null;
    }

    public void Dispose()
    {
        Stop();
    }
}
