using HyperTool.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace HyperTool.Services;

public sealed class HyperVControlChannel : IAsyncDisposable, IDisposable
{
    private const string EnsureUsbSharedCommand = "ensure-usb-shared";
    private const string GuestVmNetworkOverviewCommand = "guest-vm-network-overview";
    private const string GuestVmNetworkSwitchCommand = "guest-vm-network-switch";
    private static readonly HyperVSocketConnectionOptions HostIdentityConnectionOptions = new()
    {
        ConnectTimeout = TimeSpan.FromMilliseconds(3000),
        RequestTimeout = TimeSpan.FromSeconds(45),
        MaxConnectAttempts = 4,
        InitialBackoff = TimeSpan.FromMilliseconds(120),
        MaxBackoff = TimeSpan.FromSeconds(8),
        NoBufferCircuitCooldown = TimeSpan.FromSeconds(12)
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly PersistentHyperVConnection _hostIdentityConnection;
    private readonly PersistentHyperVConnection _sharedCatalogConnection;
    private readonly SemaphoreSlim _hostIdentityCoalesceGate = new(1, 1);
    private readonly SemaphoreSlim _sharedCatalogCoalesceGate = new(1, 1);
    private Task<HostIdentityInfo?>? _inflightHostIdentity;
    private Task<IReadOnlyList<HostSharedFolderDefinition>>? _inflightSharedCatalog;
    private HostIdentityInfo? _cachedHostIdentity;
    private DateTimeOffset _hostIdentityCacheUntilUtc = DateTimeOffset.MinValue;

    public HyperVControlChannel(Guid? hostIdentityServiceId = null, Guid? sharedCatalogServiceId = null)
    {
        _hostIdentityConnection = new PersistentHyperVConnection(
            hostIdentityServiceId ?? HyperVSocketUsbTunnelDefaults.HostIdentityServiceId,
            "control-hostidentity",
            HostIdentityConnectionOptions);
        _sharedCatalogConnection = new PersistentHyperVConnection(
            sharedCatalogServiceId ?? HyperVSocketUsbTunnelDefaults.SharedFolderCatalogServiceId,
            "control-sharedcatalog");
    }

    public async Task<HostIdentityInfo?> FetchHostIdentityAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedHostIdentity is not null && DateTimeOffset.UtcNow <= _hostIdentityCacheUntilUtc)
        {
            return _cachedHostIdentity;
        }

        await _hostIdentityCoalesceGate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _cachedHostIdentity is not null && DateTimeOffset.UtcNow <= _hostIdentityCacheUntilUtc)
            {
                return _cachedHostIdentity;
            }

            if (_inflightHostIdentity is { IsCompleted: false } inflight)
            {
                HyperVSocketConnectionMetrics.OnRequestCoalesced();
                return await inflight;
            }

            _inflightHostIdentity = FetchHostIdentityCoreAsync(cancellationToken);
            return await _inflightHostIdentity;
        }
        finally
        {
            _hostIdentityCoalesceGate.Release();
        }
    }

    private async Task<HostIdentityInfo?> FetchHostIdentityCoreAsync(CancellationToken cancellationToken)
    {
        var payload = await _hostIdentityConnection.SendAndReceiveLineAsync("{}", cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var parsed = JsonSerializer.Deserialize<HostIdentityWirePayload>(payload, JsonOptions);
        if (parsed is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(parsed.HostName) && string.IsNullOrWhiteSpace(parsed.Fqdn))
        {
            return null;
        }

        _cachedHostIdentity = new HostIdentityInfo
        {
            HostName = parsed.HostName?.Trim() ?? string.Empty,
            Fqdn = parsed.Fqdn?.Trim() ?? string.Empty,
            Features = parsed.Features ?? new HostFeatureAvailability()
        };
        _hostIdentityCacheUntilUtc = DateTimeOffset.UtcNow.AddSeconds(45);
        return _cachedHostIdentity;
    }

    public async Task<IReadOnlyList<HostSharedFolderDefinition>> FetchSharedFolderCatalogAsync(CancellationToken cancellationToken)
    {
        await _sharedCatalogCoalesceGate.WaitAsync(cancellationToken);
        try
        {
            if (_inflightSharedCatalog is { IsCompleted: false } inflight)
            {
                HyperVSocketConnectionMetrics.OnRequestCoalesced();
                return await inflight;
            }

            _inflightSharedCatalog = FetchSharedFolderCatalogCoreAsync(cancellationToken);
            return await _inflightSharedCatalog;
        }
        finally
        {
            _sharedCatalogCoalesceGate.Release();
        }
    }

    public async Task<HostUsbShareCommandResult> EnsureHostUsbSharedAsync(
        HostUsbShareCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var normalizedBusId = (request.BusId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedBusId))
        {
            return new HostUsbShareCommandResult
            {
                Success = false,
                AlreadyShared = false,
                BusId = string.Empty,
                ErrorCode = "invalid_busid",
                Message = "BUSID fehlt."
            };
        }

        var requestPayload = new
        {
            busId = normalizedBusId,
            sourceVmId = (request.SourceVmId ?? string.Empty).Trim(),
            guestComputerName = (request.GuestComputerName ?? string.Empty).Trim()
        };

        return await SendHostIdentityCommandAsync(
            EnsureUsbSharedCommand,
            requestPayload,
            cancellationToken,
            () => new HostUsbShareCommandResult
            {
                Success = false,
                AlreadyShared = false,
                BusId = normalizedBusId,
                ErrorCode = "no_response",
                Message = "Keine Antwort vom Host erhalten."
            },
            () => new HostUsbShareCommandResult
            {
                Success = false,
                AlreadyShared = false,
                BusId = normalizedBusId,
                ErrorCode = "unsupported",
                Message = "Host unterstützt Guest-initiierte USB-Freigabe nicht."
            },
            () => new HostUsbShareCommandResult
            {
                Success = false,
                AlreadyShared = false,
                BusId = normalizedBusId,
                ErrorCode = "invalid_response",
                Message = "Host-Antwort konnte nicht ausgewertet werden."
            },
            result =>
            {
                result.BusId = string.IsNullOrWhiteSpace(result.BusId)
                    ? normalizedBusId
                    : result.BusId.Trim();
                result.ErrorCode = (result.ErrorCode ?? string.Empty).Trim();
                result.Message = (result.Message ?? string.Empty).Trim();
                return result;
            });
    }

    public async Task<GuestVmNetworkOverviewResult> FetchGuestVmNetworkOverviewAsync(
        GuestVmNetworkOverviewRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var normalizedGuestComputerName = (request.GuestComputerName ?? string.Empty).Trim();
        var normalizedSourceVmId = (request.SourceVmId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(normalizedGuestComputerName)
            && string.IsNullOrWhiteSpace(normalizedSourceVmId))
        {
            return new GuestVmNetworkOverviewResult
            {
                Success = false,
                ErrorCode = "identity_missing",
                Message = "Guest-Identität fehlt."
            };
        }

        var requestPayload = new
        {
            sourceVmId = normalizedSourceVmId,
            guestComputerName = normalizedGuestComputerName
        };

        return await SendHostIdentityCommandAsync(
            GuestVmNetworkOverviewCommand,
            requestPayload,
            cancellationToken,
            () => new GuestVmNetworkOverviewResult
            {
                Success = false,
                ErrorCode = "no_response",
                Message = "Keine Antwort vom Host erhalten."
            },
            () => new GuestVmNetworkOverviewResult
            {
                Success = false,
                ErrorCode = "unsupported",
                Message = "Host unterstützt Guest-VM-Netzwerkabfrage nicht."
            },
            () => new GuestVmNetworkOverviewResult
            {
                Success = false,
                ErrorCode = "invalid_response",
                Message = "Host-Antwort konnte nicht ausgewertet werden."
            },
            result => NormalizeNetworkOverviewResult(result));
    }

    public async Task<GuestVmNetworkSwitchCommandResult> SwitchGuestVmNetworkAdapterAsync(
        GuestVmNetworkSwitchCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var normalizedAdapterName = (request.AdapterName ?? string.Empty).Trim();
        var normalizedSwitchName = (request.SwitchName ?? string.Empty).Trim();
        var normalizedGuestComputerName = (request.GuestComputerName ?? string.Empty).Trim();
        var normalizedSourceVmId = (request.SourceVmId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(normalizedAdapterName)
            || string.IsNullOrWhiteSpace(normalizedSwitchName))
        {
            return new GuestVmNetworkSwitchCommandResult
            {
                Success = false,
                AdapterName = normalizedAdapterName,
                SwitchName = normalizedSwitchName,
                ErrorCode = "invalid_request",
                Message = "Adapter- und Switch-Name sind erforderlich."
            };
        }

        if (string.IsNullOrWhiteSpace(normalizedGuestComputerName)
            && string.IsNullOrWhiteSpace(normalizedSourceVmId))
        {
            return new GuestVmNetworkSwitchCommandResult
            {
                Success = false,
                AdapterName = normalizedAdapterName,
                SwitchName = normalizedSwitchName,
                ErrorCode = "identity_missing",
                Message = "Guest-Identität fehlt."
            };
        }

        var requestPayload = new
        {
            sourceVmId = normalizedSourceVmId,
            guestComputerName = normalizedGuestComputerName,
            adapterName = normalizedAdapterName,
            switchName = normalizedSwitchName
        };

        return await SendHostIdentityCommandAsync(
            GuestVmNetworkSwitchCommand,
            requestPayload,
            cancellationToken,
            () => new GuestVmNetworkSwitchCommandResult
            {
                Success = false,
                AdapterName = normalizedAdapterName,
                SwitchName = normalizedSwitchName,
                ErrorCode = "no_response",
                Message = "Keine Antwort vom Host erhalten."
            },
            () => new GuestVmNetworkSwitchCommandResult
            {
                Success = false,
                AdapterName = normalizedAdapterName,
                SwitchName = normalizedSwitchName,
                ErrorCode = "unsupported",
                Message = "Host unterstützt Guest-VM-Switch-Wechsel nicht."
            },
            () => new GuestVmNetworkSwitchCommandResult
            {
                Success = false,
                AdapterName = normalizedAdapterName,
                SwitchName = normalizedSwitchName,
                ErrorCode = "invalid_response",
                Message = "Host-Antwort konnte nicht ausgewertet werden."
            },
            result =>
            {
                result.AdapterName = string.IsNullOrWhiteSpace(result.AdapterName)
                    ? normalizedAdapterName
                    : result.AdapterName.Trim();
                result.SwitchName = string.IsNullOrWhiteSpace(result.SwitchName)
                    ? normalizedSwitchName
                    : result.SwitchName.Trim();
                result.VmName = (result.VmName ?? string.Empty).Trim();
                result.VmId = (result.VmId ?? string.Empty).Trim();
                result.ErrorCode = (result.ErrorCode ?? string.Empty).Trim();
                result.Message = (result.Message ?? string.Empty).Trim();
                return result;
            });
    }

    private async Task<TResult> SendHostIdentityCommandAsync<TResult>(
        string command,
        object requestPayload,
        CancellationToken cancellationToken,
        Func<TResult> noResponseFactory,
        Func<TResult> unsupportedFactory,
        Func<TResult> invalidResponseFactory,
        Func<TResult, TResult> normalizeResult)
        where TResult : class
    {
        var wirePayload = JsonSerializer.Serialize(new
        {
            command,
            payload = requestPayload
        }, JsonOptions);

        var responsePayload = await _hostIdentityConnection.SendAndReceiveLineAsync(wirePayload, cancellationToken);
        if (string.IsNullOrWhiteSpace(responsePayload))
        {
            return noResponseFactory();
        }

        try
        {
            var response = JsonSerializer.Deserialize<HostIdentityCommandWireEnvelope>(responsePayload, JsonOptions);
            if (response is null
                || !string.Equals(response.Type, "command-result", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(response.Command, command, StringComparison.OrdinalIgnoreCase)
                || response.Result.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                return unsupportedFactory();
            }

            var parsedResult = JsonSerializer.Deserialize<TResult>(response.Result.GetRawText(), JsonOptions);
            if (parsedResult is null)
            {
                return invalidResponseFactory();
            }

            return normalizeResult(parsedResult);
        }
        catch
        {
            return invalidResponseFactory();
        }
    }

    private static GuestVmNetworkOverviewResult NormalizeNetworkOverviewResult(GuestVmNetworkOverviewResult result)
    {
        result.VmName = (result.VmName ?? string.Empty).Trim();
        result.VmId = (result.VmId ?? string.Empty).Trim();
        result.ErrorCode = (result.ErrorCode ?? string.Empty).Trim();
        result.Message = (result.Message ?? string.Empty).Trim();

        result.Adapters = (result.Adapters ?? [])
            .Where(static adapter => adapter is not null)
            .Select(static adapter => new HostVmNetworkAdapterInfo
            {
                Name = (adapter.Name ?? string.Empty).Trim(),
                SwitchName = (adapter.SwitchName ?? string.Empty).Trim(),
                MacAddress = (adapter.MacAddress ?? string.Empty).Trim(),
                IpAddresses = (adapter.IpAddresses ?? [])
                    .Where(static address => !string.IsNullOrWhiteSpace(address))
                    .Select(static address => address.Trim())
                    .ToList(),
                Ipv4Address = (adapter.Ipv4Address ?? string.Empty).Trim(),
                Ipv4SubnetMask = (adapter.Ipv4SubnetMask ?? string.Empty).Trim(),
                Ipv4Gateway = (adapter.Ipv4Gateway ?? string.Empty).Trim(),
                GuestComputerName = (adapter.GuestComputerName ?? string.Empty).Trim()
            })
            .OrderBy(static adapter => string.IsNullOrWhiteSpace(adapter.Name) ? "Network Adapter" : adapter.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        result.Switches = (result.Switches ?? [])
            .Where(static vmSwitch => vmSwitch is not null && !string.IsNullOrWhiteSpace(vmSwitch.Name))
            .Select(static vmSwitch => new HostVmSwitchInfo
            {
                Name = (vmSwitch.Name ?? string.Empty).Trim(),
                SwitchType = (vmSwitch.SwitchType ?? string.Empty).Trim(),
                NetAdapterInterfaceDescription = (vmSwitch.NetAdapterInterfaceDescription ?? string.Empty).Trim()
            })
            .OrderBy(static vmSwitch => vmSwitch.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return result;
    }

    private async Task<IReadOnlyList<HostSharedFolderDefinition>> FetchSharedFolderCatalogCoreAsync(CancellationToken cancellationToken)
    {
        var payload = await _sharedCatalogConnection.SendAndReceiveLineAsync("{}", cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        var catalog = JsonSerializer.Deserialize<List<HostSharedFolderDefinition>>(payload, JsonOptions) ?? [];
        return catalog
            .Where(static item => item is not null
                                  && !string.IsNullOrWhiteSpace(item.Id)
                                  && !string.IsNullOrWhiteSpace(item.ShareName))
            .Select(static item => new HostSharedFolderDefinition
            {
                Id = item.Id,
                Label = item.Label,
                LocalPath = item.LocalPath,
                ShareName = item.ShareName,
                Enabled = item.Enabled,
                ReadOnly = item.ReadOnly
            })
            .ToList();
    }

    public void Dispose()
    {
        _hostIdentityConnection.Dispose();
        _sharedCatalogConnection.Dispose();
        _hostIdentityCoalesceGate.Dispose();
        _sharedCatalogCoalesceGate.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class HostIdentityWirePayload
    {
        public string HostName { get; set; } = string.Empty;
        public string Fqdn { get; set; } = string.Empty;
        public HostFeatureAvailability? Features { get; set; }
    }

    private sealed class HostIdentityCommandWireEnvelope
    {
        public string Type { get; set; } = string.Empty;

        public string Command { get; set; } = string.Empty;

        public JsonElement Result { get; set; }
    }
}
