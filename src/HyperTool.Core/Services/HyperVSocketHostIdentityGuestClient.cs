using HyperTool.Models;

namespace HyperTool.Services;

public sealed class HyperVSocketHostIdentityGuestClient
{
    private static readonly HyperVControlChannel SharedControlChannel = new();
    private readonly HyperVControlChannel _controlChannel;

    public HyperVSocketHostIdentityGuestClient(Guid? serviceId = null)
    {
        _controlChannel = serviceId.HasValue
            ? new HyperVControlChannel(hostIdentityServiceId: serviceId)
            : SharedControlChannel;
    }

    public async Task<string?> FetchHostNameAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        var identity = await FetchHostIdentityAsync(cancellationToken, forceRefresh);
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

    public Task<HostIdentityInfo?> FetchHostIdentityAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        return _controlChannel.FetchHostIdentityAsync(cancellationToken, forceRefresh);
    }

    public Task<HostUsbShareCommandResult> EnsureHostUsbSharedAsync(
        HostUsbShareCommandRequest request,
        CancellationToken cancellationToken)
    {
        return _controlChannel.EnsureHostUsbSharedAsync(request, cancellationToken);
    }
}
