namespace HyperTool.Models;

public sealed class VmDefinition
{
    public string Name { get; set; } = string.Empty;

    public string VmId { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? Name : Label;

    public string RuntimeState { get; set; } = "Unbekannt";

    public string RuntimeSwitchName { get; set; } = "-";

    public bool HasMountedIso { get; set; }

    public string MountedIsoPath { get; set; } = string.Empty;

    public string MonitorStateText { get; set; } = "Guest nicht erreichbar";

    public string MonitorCpuText { get; set; } = "CPU -";

    public string MonitorRamText { get; set; } = "RAM -";

    public string TrayAdapterName { get; set; } = string.Empty;

    public bool OpenConsoleWithSessionEdit { get; set; }

    public override string ToString() => DisplayLabel;
}