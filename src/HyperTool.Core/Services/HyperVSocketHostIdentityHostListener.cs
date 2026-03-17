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
    private const int MaxConcurrentClients = 24;
    private readonly Guid _serviceId;
    private readonly Func<HostFeatureAvailability>? _featureAvailabilityProvider;
    private readonly Func<IReadOnlyList<UsbDeviceMetadataEntry>>? _usbMetadataProvider;
    private readonly Func<IReadOnlyList<UsbDeviceHostDescriptionEntry>>? _usbDescriptionProvider;
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
        Func<IReadOnlyList<UsbDeviceHostDescriptionEntry>>? usbDescriptionProvider = null)
    {
        _serviceId = serviceId ?? HyperVSocketUsbTunnelDefaults.HostIdentityServiceId;
        _featureAvailabilityProvider = featureAvailabilityProvider;
        _usbMetadataProvider = usbMetadataProvider;
        _usbDescriptionProvider = usbDescriptionProvider;
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
            await using var stream = new NetworkStream(socket, ownsSocket: true);

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
                Comment = (entry.Comment ?? string.Empty).Trim()
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.DeviceKey)
                            && (!string.IsNullOrWhiteSpace(entry.CustomName)
                                || !string.IsNullOrWhiteSpace(entry.Comment)))
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

            var payload = JsonSerializer.Serialize(new
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

            await using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 1024, leaveOpen: false)
            {
                NewLine = "\n"
            };

            await writer.WriteLineAsync(payload.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _clientHandlerGate.Release();
        }
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
            Comment = (source.Comment ?? string.Empty).Trim()
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
