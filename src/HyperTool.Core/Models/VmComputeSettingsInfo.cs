namespace HyperTool.Models;

public sealed class VmComputeSettingsInfo
{
    public int CpuCount { get; set; } = 1;

    public int StartupMemoryGb { get; set; } = 2;

    public bool DynamicMemoryEnabled { get; set; }

    public int MinCpuCount { get; set; } = 1;

    public int MaxCpuCount { get; set; } = 1;

    public int MinStartupMemoryGb { get; set; } = 1;

    public int MaxStartupMemoryGb { get; set; } = 2;

    public int HostLogicalProcessorCount { get; set; } = 1;

    public int HostTotalMemoryGb { get; set; } = 2;
}