namespace HyperTool.Models;

public sealed class HostFeatureAvailability
{
    public bool UsbSharingEnabled { get; set; } = true;

    public bool SharedFoldersEnabled { get; set; } = true;

    public List<UsbDeviceMetadataEntry> UsbDeviceMetadata { get; set; } = [];

    public List<UsbDeviceHostDescriptionEntry> UsbDeviceDescriptions { get; set; } = [];

    public List<UsbDeviceAttachmentEntry> UsbDeviceAttachments { get; set; } = [];
}

public sealed class UsbDeviceHostDescriptionEntry
{
    public string DeviceKey { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

public sealed class UsbDeviceAttachmentEntry
{
    public string BusId { get; set; } = string.Empty;

    public string GuestComputerName { get; set; } = string.Empty;

    public string SourceVmId { get; set; } = string.Empty;

    public string GuestVmName { get; set; } = string.Empty;

    public string ClientIpAddress { get; set; } = string.Empty;
}

public sealed class HostIdentityInfo
{
    public string HostName { get; set; } = string.Empty;

    public string Fqdn { get; set; } = string.Empty;

    public HostFeatureAvailability Features { get; set; } = new();
}

public sealed class HostUsbShareCommandRequest
{
    public string BusId { get; set; } = string.Empty;

    public string SourceVmId { get; set; } = string.Empty;

    public string GuestComputerName { get; set; } = string.Empty;
}

public sealed class HostUsbShareCommandResult
{
    public bool Success { get; set; }

    public bool AlreadyShared { get; set; }

    public string BusId { get; set; } = string.Empty;

    public string ErrorCode { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public sealed class GuestVmNetworkOverviewRequest
{
    public string SourceVmId { get; set; } = string.Empty;

    public string GuestComputerName { get; set; } = string.Empty;
}

public sealed class GuestVmNetworkOverviewResult
{
    public bool Success { get; set; }

    public string VmName { get; set; } = string.Empty;

    public string VmId { get; set; } = string.Empty;

    public string ErrorCode { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public List<HostVmNetworkAdapterInfo> Adapters { get; set; } = [];

    public List<HostVmSwitchInfo> Switches { get; set; } = [];
}

public sealed class HostVmNetworkAdapterInfo
{
    public string Name { get; set; } = string.Empty;

    public string SwitchName { get; set; } = string.Empty;

    public string MacAddress { get; set; } = string.Empty;

    public List<string> IpAddresses { get; set; } = [];

    public string Ipv4Address { get; set; } = string.Empty;

    public string Ipv4SubnetMask { get; set; } = string.Empty;

    public string Ipv4Gateway { get; set; } = string.Empty;

    public string GuestComputerName { get; set; } = string.Empty;
}

public sealed class HostVmSwitchInfo
{
    public string Name { get; set; } = string.Empty;

    public string SwitchType { get; set; } = string.Empty;

    public string NetAdapterInterfaceDescription { get; set; } = string.Empty;
}

public sealed class GuestVmNetworkSwitchCommandRequest
{
    public string SourceVmId { get; set; } = string.Empty;

    public string GuestComputerName { get; set; } = string.Empty;

    public string AdapterName { get; set; } = string.Empty;

    public string SwitchName { get; set; } = string.Empty;
}

public sealed class GuestVmNetworkSwitchCommandResult
{
    public bool Success { get; set; }

    public string VmName { get; set; } = string.Empty;

    public string VmId { get; set; } = string.Empty;

    public string AdapterName { get; set; } = string.Empty;

    public string SwitchName { get; set; } = string.Empty;

    public string ErrorCode { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}