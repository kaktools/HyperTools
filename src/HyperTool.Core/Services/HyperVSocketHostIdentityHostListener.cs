using Microsoft.Win32;
using HyperTool.Models;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Linq;

namespace HyperTool.Services;

public sealed class HyperVSocketHostIdentityHostListener : IDisposable
{
    private const string EnsureUsbSharedCommand = "ensure-usb-shared";
    private const int MaxConcurrentClients = 24;
    private readonly Guid _serviceId;
    private readonly Func<HostFeatureAvailability>? _featureAvailabilityProvider;
    private readonly Func<IReadOnlyList<UsbDeviceMetadataEntry>>? _usbMetadataProvider;
    private readonly Func<IReadOnlyList<UsbDeviceHostDescriptionEntry>>? _usbDescriptionProvider;
    private readonly Func<HostUsbShareCommandRequest, CancellationToken, Task<HostUsbShareCommandResult>>? _usbShareCommandHandler;
    private readonly object _snapshotSync = new();
    private HostFeatureAvailability _lastFeatureAvailability = new();
    private List<UsbDeviceMetadataEntry> _lastUsbMetadataSnapshot = [];
    private List<UsbDeviceHostDescriptionEntry> _lastUsbDescriptionSnapshot = [];
    private readonly SemaphoreSlim _clientHandlerGate = new(MaxConcurrentClients, MaxConcurrentClients);
    private Socket? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public HyperVSocketHostIdentityHostListener(
        Guid? serviceId = null,
        Func<HostFeatureAvailability>? featureAvailabilityProvider = null,
        Func<IReadOnlyList<UsbDeviceMetadataEntry>>? usbMetadataProvider = null,
        Func<IReadOnlyList<UsbDeviceHostDescriptionEntry>>? usbDescriptionProvider = null,
        Func<HostUsbShareCommandRequest, CancellationToken, Task<HostUsbShareCommandResult>>? usbShareCommandHandler = null)
    {
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.HostIdentityServiceId;
        _featureAvailabilityProvider = featureAvailabilityProvider;
        _usbMetadataProvider = usbMetadataProvider;
        _usbDescriptionProvider = usbDescriptionProvider;
        _usbShareCommandHandler = usbShareCommandHandler;
    }

    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        if (!EnsureServiceGuidRegistration())
        {
            throw new InvalidOperationException(
                "Hyper-V Socket Host-Identity-Dienst ist nicht registriert. Starte HyperTool Host als Administrator, um den Dienst einmalig zu registrieren.");
        }

        var listener = new Socket((AddressFamily)34, SocketType.Stream, (ProtocolType)1);
        listener.Bind(new HyperVSocketEndPoint(HyperVSocketUsbTunnelDefaults.VmIdWildcard, _serviceId));
        listener.Listen(16);

        _listener = listener;
        _cts = new CancellationTokenSource();
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        IsRunning = true;
    }

    private bool EnsureServiceGuidRegistration()
    {
        if (IsServiceGuidRegistered())
        {
            return true;
        }

        if (TryRegisterServiceGuid())
        {
            return true;
        }

        return IsServiceGuidRegistered();
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
                SafeFireAndForget.Run(HandleClientAsync(socket, cancellationToken), operation: "host-identity-listener-client");
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
            await using var stream = new NetworkStream(socket, ownsSocket: true);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            await using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 1024, leaveOpen: false)
            {
                NewLine = "\n"
            };

            while (!cancellationToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    break;
                }

                if (line is null)
                {
                    break;
                }

                var payload = await BuildResponsePayloadAsync(line, cancellationToken);
                await writer.WriteLineAsync(payload.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
            }
        }
        finally
        {
            _clientHandlerGate.Release();
        }
    }

    private async Task<string> BuildResponsePayloadAsync(string line, CancellationToken cancellationToken)
    {
        var commandRequest = TryParseEnsureUsbSharedCommand(line);
        if (commandRequest is null)
        {
            return BuildIdentityPayload();
        }

        if (_usbShareCommandHandler is null)
        {
            return BuildCommandResultPayload(
                EnsureUsbSharedCommand,
                new HostUsbShareCommandResult
                {
                    Success = false,
                    AlreadyShared = false,
                    BusId = (commandRequest.BusId ?? string.Empty).Trim(),
                    ErrorCode = "unsupported",
                    Message = "Host unterstützt Guest-initiierte USB-Freigabe nicht."
                });
        }

        HostUsbShareCommandResult result;
        try
        {
            result = await _usbShareCommandHandler(commandRequest, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = new HostUsbShareCommandResult
            {
                Success = false,
                AlreadyShared = false,
                BusId = (commandRequest.BusId ?? string.Empty).Trim(),
                ErrorCode = "host_exception",
                Message = string.IsNullOrWhiteSpace(ex.Message) ? "Host-Fehler bei USB-Freigabe." : ex.Message.Trim()
            };
        }

        return BuildCommandResultPayload(EnsureUsbSharedCommand, result);
    }

    private static HostUsbShareCommandRequest? TryParseEnsureUsbSharedCommand(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var command = GetJsonString(doc.RootElement, "command")?.Trim();
            if (!string.Equals(command, EnsureUsbSharedCommand, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new HostUsbShareCommandRequest
            {
                BusId = GetJsonString(doc.RootElement, "busId")?.Trim() ?? string.Empty,
                SourceVmId = GetJsonString(doc.RootElement, "sourceVmId")?.Trim() ?? string.Empty,
                GuestComputerName = GetJsonString(doc.RootElement, "guestComputerName")?.Trim() ?? string.Empty
            };
        }
        catch
        {
            return null;
        }
    }

    private static string BuildCommandResultPayload(string command, HostUsbShareCommandResult result)
    {
        return JsonSerializer.Serialize(new
        {
            type = "command-result",
            command,
            result = new HostUsbShareCommandResult
            {
                Success = result.Success,
                AlreadyShared = result.AlreadyShared,
                BusId = (result.BusId ?? string.Empty).Trim(),
                ErrorCode = (result.ErrorCode ?? string.Empty).Trim(),
                Message = (result.Message ?? string.Empty).Trim()
            },
            timestampUtc = DateTime.UtcNow
        }, SerializerOptions);
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

    private string BuildIdentityPayload()
    {
        HostFeatureAvailability featureAvailability;
        try
        {
            featureAvailability = _featureAvailabilityProvider?.Invoke() ?? new HostFeatureAvailability();
            lock (_snapshotSync)
            {
                _lastFeatureAvailability = CloneFeatureAvailability(featureAvailability);
            }
        }
        catch
        {
            lock (_snapshotSync)
            {
                featureAvailability = CloneFeatureAvailability(_lastFeatureAvailability);
            }
        }

        IReadOnlyList<UsbDeviceMetadataEntry> usbMetadata;
        try
        {
            usbMetadata = _usbMetadataProvider?.Invoke() ?? [];
            lock (_snapshotSync)
            {
                _lastUsbMetadataSnapshot = usbMetadata
                    .Where(entry => entry is not null)
                    .Select(CloneMetadataEntry)
                    .ToList();
            }
        }
        catch
        {
            lock (_snapshotSync)
            {
                usbMetadata = _lastUsbMetadataSnapshot
                    .Select(CloneMetadataEntry)
                    .ToList();
            }
        }

        IReadOnlyList<UsbDeviceHostDescriptionEntry> usbDescriptions;
        try
        {
            usbDescriptions = _usbDescriptionProvider?.Invoke() ?? [];
            lock (_snapshotSync)
            {
                _lastUsbDescriptionSnapshot = usbDescriptions
                    .Where(entry => entry is not null)
                    .Select(CloneDescriptionEntry)
                    .ToList();
            }
        }
        catch
        {
            lock (_snapshotSync)
            {
                usbDescriptions = _lastUsbDescriptionSnapshot
                    .Select(CloneDescriptionEntry)
                    .ToList();
            }
        }

        featureAvailability.UsbDeviceMetadata = usbMetadata
            .Where(entry => entry is not null)
            .Select(entry => new UsbDeviceMetadataEntry
            {
                DeviceKey = (entry.DeviceKey ?? string.Empty).Trim(),
                CustomName = (entry.CustomName ?? string.Empty).Trim(),
                Comment = (entry.Comment ?? string.Empty).Trim(),
                BlockInGuest = entry.BlockInGuest
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.DeviceKey)
                            && (!string.IsNullOrWhiteSpace(entry.CustomName)
                                || !string.IsNullOrWhiteSpace(entry.Comment)
                                || entry.BlockInGuest))
            .ToList();

        featureAvailability.UsbDeviceDescriptions = usbDescriptions
            .Where(entry => entry is not null)
            .Select(entry => new UsbDeviceHostDescriptionEntry
            {
                DeviceKey = (entry.DeviceKey ?? string.Empty).Trim(),
                Description = (entry.Description ?? string.Empty).Trim()
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.DeviceKey)
                            && !string.IsNullOrWhiteSpace(entry.Description))
            .ToList();

        featureAvailability.UsbDeviceAttachments = (featureAvailability.UsbDeviceAttachments ?? [])
            .Where(entry => entry is not null)
            .Select(entry => new UsbDeviceAttachmentEntry
            {
                BusId = (entry.BusId ?? string.Empty).Trim(),
                GuestComputerName = (entry.GuestComputerName ?? string.Empty).Trim(),
                SourceVmId = (entry.SourceVmId ?? string.Empty).Trim(),
                GuestVmName = (entry.GuestVmName ?? string.Empty).Trim(),
                ClientIpAddress = (entry.ClientIpAddress ?? string.Empty).Trim()
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.BusId))
            .ToList();

        return JsonSerializer.Serialize(new
        {
            hostName = Environment.MachineName,
            fqdn = ResolveFqdn(),
            features = new
            {
                usbSharingEnabled = featureAvailability.UsbSharingEnabled,
                sharedFoldersEnabled = featureAvailability.SharedFoldersEnabled,
                usbDeviceMetadata = featureAvailability.UsbDeviceMetadata,
                usbDeviceDescriptions = featureAvailability.UsbDeviceDescriptions,
                usbDeviceAttachments = featureAvailability.UsbDeviceAttachments
            },
            timestampUtc = DateTime.UtcNow
        }, SerializerOptions);
    }

    private static string ResolveFqdn()
    {
        try
        {
            var host = Dns.GetHostEntry(Environment.MachineName);
            return host.HostName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static HostFeatureAvailability CloneFeatureAvailability(HostFeatureAvailability source)
    {
        return new HostFeatureAvailability
        {
            UsbSharingEnabled = source.UsbSharingEnabled,
            SharedFoldersEnabled = source.SharedFoldersEnabled,
            UsbDeviceMetadata = (source.UsbDeviceMetadata ?? [])
                .Where(entry => entry is not null)
                .Select(CloneMetadataEntry)
                .ToList(),
            UsbDeviceDescriptions = (source.UsbDeviceDescriptions ?? [])
                .Where(entry => entry is not null)
                .Select(CloneDescriptionEntry)
                .ToList(),
            UsbDeviceAttachments = (source.UsbDeviceAttachments ?? [])
                .Where(entry => entry is not null)
                .Select(entry => new UsbDeviceAttachmentEntry
                {
                    BusId = (entry.BusId ?? string.Empty).Trim(),
                    GuestComputerName = (entry.GuestComputerName ?? string.Empty).Trim(),
                    SourceVmId = (entry.SourceVmId ?? string.Empty).Trim(),
                    GuestVmName = (entry.GuestVmName ?? string.Empty).Trim(),
                    ClientIpAddress = (entry.ClientIpAddress ?? string.Empty).Trim()
                })
                .ToList()
        };
    }

    private static UsbDeviceMetadataEntry CloneMetadataEntry(UsbDeviceMetadataEntry source)
    {
        return new UsbDeviceMetadataEntry
        {
            DeviceKey = (source.DeviceKey ?? string.Empty).Trim(),
            CustomName = (source.CustomName ?? string.Empty).Trim(),
            Comment = (source.Comment ?? string.Empty).Trim(),
            BlockInGuest = source.BlockInGuest
        };
    }

    private static UsbDeviceHostDescriptionEntry CloneDescriptionEntry(UsbDeviceHostDescriptionEntry source)
    {
        return new UsbDeviceHostDescriptionEntry
        {
            DeviceKey = (source.DeviceKey ?? string.Empty).Trim(),
            Description = (source.Description ?? string.Empty).Trim()
        };
    }

    private bool TryRegisterServiceGuid()
    {
        try
        {
            const string rootPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization\GuestCommunicationServices";

            using var rootKey = Registry.LocalMachine.CreateSubKey(rootPath, writable: true);
            if (rootKey is null)
            {
                return false;
            }

            using var serviceKey = rootKey.CreateSubKey(_serviceId.ToString("D"), writable: true);
            serviceKey?.SetValue("ElementName", "HyperTool Hyper-V Socket Host Identity", RegistryValueKind.String);
            return serviceKey is not null;
        }
        catch
        {
            return false;
        }
    }

    private bool IsServiceGuidRegistered()
    {
        try
        {
            const string rootPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization\GuestCommunicationServices";

            using var rootKey = Registry.LocalMachine.OpenSubKey(rootPath, writable: false);
            if (rootKey is null)
            {
                return false;
            }

            using var serviceKey = rootKey.OpenSubKey(_serviceId.ToString("D"), writable: false);
            return serviceKey is not null;
        }
        catch
        {
            return false;
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
