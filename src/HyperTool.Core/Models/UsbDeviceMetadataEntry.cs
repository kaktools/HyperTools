namespace HyperTool.Models;

public sealed class UsbDeviceMetadataEntry
{
    public string DeviceKey { get; set; } = string.Empty;

    public string CustomName { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;
}