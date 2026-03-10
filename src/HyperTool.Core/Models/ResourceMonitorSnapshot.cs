namespace HyperTool.Models;

public sealed class VmResourceMonitorSnapshot
{
    public string VmName { get; set; } = string.Empty;

    public string State { get; set; } = "OFF";

    public double CpuPercent { get; set; }

    public double RamUsedGb { get; set; }

    public double RamTotalGb { get; set; }

    public double RamPressurePercent { get; set; }

    public IReadOnlyList<double> CpuHistory { get; set; } = [];

    public IReadOnlyList<double> RamPressureHistory { get; set; } = [];
}

public sealed class ResourceMonitorSnapshot
{
    public bool Enabled { get; set; }

    public int IntervalMs { get; set; } = 1000;

    public int HistorySize { get; set; } = 300;

    public double HostCpuPercent { get; set; }

    public double HostRamUsedGb { get; set; }

    public double HostRamTotalGb { get; set; }

    public double HostRamPressurePercent { get; set; }

    public IReadOnlyList<double> HostCpuHistory { get; set; } = [];

    public IReadOnlyList<double> HostRamPressureHistory { get; set; } = [];

    public IReadOnlyList<VmResourceMonitorSnapshot> VmSnapshots { get; set; } = [];
}
