using HyperTool.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace HyperTool.Services;

public sealed class HyperVControlChannel : IAsyncDisposable, IDisposable
{
    private const string EnsureUsbSharedCommand = "ensure-usb-shared";
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
            "control-hostidentity");
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

        var requestPayload = JsonSerializer.Serialize(new
        {
            command = EnsureUsbSharedCommand,
            busId = normalizedBusId,
            sourceVmId = (request.SourceVmId ?? string.Empty).Trim(),
            guestComputerName = (request.GuestComputerName ?? string.Empty).Trim()
        }, JsonOptions);

        var responsePayload = await _hostIdentityConnection.SendAndReceiveLineAsync(requestPayload, cancellationToken);
        if (string.IsNullOrWhiteSpace(responsePayload))
        {
            return new HostUsbShareCommandResult
            {
                Success = false,
                AlreadyShared = false,
                BusId = normalizedBusId,
                ErrorCode = "no_response",
                Message = "Keine Antwort vom Host erhalten."
            };
        }

        try
        {
            var response = JsonSerializer.Deserialize<HostIdentityCommandWirePayload>(responsePayload, JsonOptions);
            if (response is null
                || !string.Equals(response.Type, "command-result", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(response.Command, EnsureUsbSharedCommand, StringComparison.OrdinalIgnoreCase)
                || response.Result is null)
            {
                return new HostUsbShareCommandResult
                {
                    Success = false,
                    AlreadyShared = false,
                    BusId = normalizedBusId,
                    ErrorCode = "unsupported",
                    Message = "Host unterstützt Guest-initiierte USB-Freigabe nicht."
                };
            }

            response.Result.BusId = string.IsNullOrWhiteSpace(response.Result.BusId)
                ? normalizedBusId
                : response.Result.BusId.Trim();
            response.Result.ErrorCode = (response.Result.ErrorCode ?? string.Empty).Trim();
            response.Result.Message = (response.Result.Message ?? string.Empty).Trim();
            return response.Result;
        }
        catch
        {
            return new HostUsbShareCommandResult
            {
                Success = false,
                AlreadyShared = false,
                BusId = normalizedBusId,
                ErrorCode = "invalid_response",
                Message = "Host-Antwort konnte nicht ausgewertet werden."
            };
        }
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

    private sealed class HostIdentityCommandWirePayload
    {
        public string Type { get; set; } = string.Empty;

        public string Command { get; set; } = string.Empty;

        public HostUsbShareCommandResult? Result { get; set; }
    }
}
