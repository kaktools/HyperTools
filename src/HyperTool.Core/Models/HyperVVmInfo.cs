namespace HyperTool.Models;

public sealed class HyperVVmInfo
{
    public string Name { get; set; } = string.Empty;

    public string VmId { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string CurrentSwitchName { get; set; } = string.Empty;

    public bool HasMountedIso { get; set; }

    public string MountedIsoPath { get; set; } = string.Empty;
}