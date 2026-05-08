namespace HyperTool.Models;

public sealed class HyperVSwitchInfo
{
    public string Name { get; set; } = string.Empty;

    public string SwitchType { get; set; } = string.Empty;

    public string NetAdapterInterfaceDescription { get; set; } = string.Empty;

    public bool AllowManagementOs { get; set; } = true;

    public override string ToString() => Name;
}