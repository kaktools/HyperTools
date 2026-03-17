using Microsoft.Win32;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HyperTool.Services;

public sealed class HyperVSocketDiagnosticsHostListener : IDisposable
{
    private const int MaxConcurrentClients = 32;
    private static readonly JsonSerializerOptions AckJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Guid _serviceId;
    private readonly Action<HyperVSocketDiagnosticsAck> _onDiagnosticsAck;
    private readonly SemaphoreSlim _clientHandlerGate = new(MaxConcurrentClients, MaxConcurrentClients);
    private Socket? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;

    public HyperVSocketDiagnosticsHostListener(Action<HyperVSocketDiagnosticsAck> onDiagnosticsAck, Guid? serviceId = null)
    {
        _onDiagnosticsAck = onDiagnosticsAck ?? throw new ArgumentNullException(nameof(onDiagnosticsAck));
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.DiagnosticsServiceId;
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
            var gateEntered = false;
            try
            {
                if (_listener is null)
                {
                    break;
                }

                await _clientHandlerGate.WaitAsync(cancellationToken);
                gateEntered = true;
                socket = await _listener.AcceptAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(socket, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (gateEntered)
                {
                    _clientHandlerGate.Release();
                }
                break;
            }
            catch
            {
                socket?.Dispose();
                if (gateEntered)
                {
                    _clientHandlerGate.Release();
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleClientAsync(Socket socket, CancellationToken cancellationToken)
    {
        try
        {
            var sourceVmId = TryGetRemoteVmId(socket);
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

            var payload = line.Trim();
            if (payload.Length == 0)
            {
                return;
            }

            var ack = ParseAckPayload(payload);
            if (!string.IsNullOrWhiteSpace(sourceVmId) && string.IsNullOrWhiteSpace(ack.SourceVmId))
            {
                ack.SourceVmId = sourceVmId;
            }

            _onDiagnosticsAck(ack);
        }
        finally
        {
            _clientHandlerGate.Release();
        }
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

    private static HyperVSocketDiagnosticsAck ParseAckPayload(string payload)
    {
        var normalizedPayload = payload.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');

        try
        {
            var parsed = JsonSerializer.Deserialize<HyperVSocketDiagnosticsAck>(normalizedPayload, AckJsonOptions);
            if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.GuestComputerName))
            {
                return parsed;
            }
        }
        catch
        {
        }

        try
        {
            using var doc = JsonDocument.Parse(normalizedPayload);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var root = doc.RootElement;
                var guestComputerName = GetJsonString(root, "guestComputerName");
                if (!string.IsNullOrWhiteSpace(guestComputerName))
                {
                    return new HyperVSocketDiagnosticsAck
                    {
                        GuestComputerName = guestComputerName,
                        HyperVSocketActive = GetJsonBool(root, "hyperVSocketActive"),
                        RegistryServiceOk = GetJsonBool(root, "registryServiceOk"),
                        BusId = GetJsonString(root, "busId"),
                        HardwareId = GetJsonString(root, "hardwareId"),
                        InstanceId = GetJsonString(root, "instanceId"),
                        PersistedGuid = GetJsonString(root, "persistedGuid"),
                        EventType = GetJsonString(root, "eventType"),
                        SentAtUtc = GetJsonString(root, "sentAtUtc"),
                        GuestIpv4Address = GetJsonString(root, "guestIpv4Address"),
                        GuestIpv4SubnetMask = GetJsonString(root, "guestIpv4SubnetMask"),
                        GuestIpv4Gateway = GetJsonString(root, "guestIpv4Gateway"),
                        GuestNetworkAdapterName = GetJsonString(root, "guestNetworkAdapterName"),
                        GuestCpuPercent = GetJsonDouble(root, "guestCpuPercent"),
                        GuestRamUsedGb = GetJsonDouble(root, "guestRamUsedGb"),
                        GuestRamTotalGb = GetJsonDouble(root, "guestRamTotalGb"),
                        GuestIpv4Entries = GetJsonIpv4Entries(root, "guestIpv4Entries")
                    };
                }
            }
        }
        catch
        {
        }

        return new HyperVSocketDiagnosticsAck
        {
            GuestComputerName = normalizedPayload,
            HyperVSocketActive = null,
            RegistryServiceOk = null,
            SentAtUtc = null
        };
    }

    private static string? GetJsonString(JsonElement root, string propertyName)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Null => null,
                _ => property.Value.ToString()
            };
        }

        return null;
    }

    private static bool? GetJsonBool(JsonElement root, string propertyName)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (property.Value.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (property.Value.ValueKind == JsonValueKind.String
                && bool.TryParse(property.Value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static double? GetJsonDouble(JsonElement root, string propertyName)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number
                && property.Value.TryGetDouble(out var numericValue))
            {
                return numericValue;
            }

            if (property.Value.ValueKind == JsonValueKind.String
                && double.TryParse(property.Value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedValue))
            {
                return parsedValue;
            }
        }

        return null;
    }

    private static List<HyperVSocketGuestIpv4Entry> GetJsonIpv4Entries(JsonElement root, string propertyName)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                || property.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var entries = new List<HyperVSocketGuestIpv4Entry>();
            foreach (var item in property.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var ip = GetJsonString(item, "ipv4Address")?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(ip))
                {
                    continue;
                }

                entries.Add(new HyperVSocketGuestIpv4Entry
                {
                    Ipv4Address = ip,
                    SubnetMask = GetJsonString(item, "subnetMask")?.Trim() ?? string.Empty,
                    Gateway = GetJsonString(item, "gateway")?.Trim() ?? string.Empty,
                    AdapterName = GetJsonString(item, "adapterName")?.Trim() ?? string.Empty
                });
            }

            return entries;
        }

        return [];
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
            serviceKey?.SetValue("ElementName", "HyperTool Hyper-V Socket Diagnostics", RegistryValueKind.String);
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

public sealed class HyperVSocketDiagnosticsAck
{
    public string GuestComputerName { get; set; } = string.Empty;

    public string SourceVmId { get; set; } = string.Empty;

    public bool? HyperVSocketActive { get; set; }

    public bool? RegistryServiceOk { get; set; }

    public string? BusId { get; set; }

    public string? HardwareId { get; set; }

    public string? InstanceId { get; set; }

    public string? PersistedGuid { get; set; }

    public string? EventType { get; set; }

    public string? SentAtUtc { get; set; }

    public string? GuestIpv4Address { get; set; }

    public string? GuestIpv4SubnetMask { get; set; }

    public string? GuestIpv4Gateway { get; set; }

    public string? GuestNetworkAdapterName { get; set; }

    public double? GuestCpuPercent { get; set; }

    public double? GuestRamUsedGb { get; set; }

    public double? GuestRamTotalGb { get; set; }

    public List<HyperVSocketGuestIpv4Entry> GuestIpv4Entries { get; set; } = [];
}

public sealed class HyperVSocketGuestIpv4Entry
{
    public string Ipv4Address { get; set; } = string.Empty;

    public string SubnetMask { get; set; } = string.Empty;

    public string Gateway { get; set; } = string.Empty;

    public string AdapterName { get; set; } = string.Empty;
}
