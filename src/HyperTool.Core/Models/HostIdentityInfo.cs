namespace HyperTool.Models;

public sealed class HostFeatureAvailability
{
    public bool UsbSharingEnabled { get; set; } = true;

    public bool SharedFoldersEnabled { get; set; } = true;

    public List<UsbDeviceMetadataEntry> UsbDeviceMetadata { get; set; } = [];
}

public sealed class HostIdentityInfo
{
    public string HostName { get; set; } = string.Empty;

    public string Fqdn { get; set; } = string.Empty;

    public HostFeatureAvailability Features { get; set; } = new();
}