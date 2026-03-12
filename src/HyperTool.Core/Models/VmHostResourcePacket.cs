namespace HyperTool.Models;

public sealed class VmHostResourcePacket
{
    public string VmName { get; set; } = string.Empty;

    public string VmId { get; set; } = string.Empty;

    public double CpuPercent { get; set; }

    public double RamUsedGb { get; set; }

    public double RamTotalGb { get; set; }

    public string SampledAtUtc { get; set; } = string.Empty;
}
