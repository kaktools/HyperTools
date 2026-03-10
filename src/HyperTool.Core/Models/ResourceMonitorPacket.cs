namespace HyperTool.Models;

public sealed class ResourceMonitorPacket
{
    public string Vm { get; set; } = string.Empty;

    public double Cpu { get; set; }

    public double RamUsed { get; set; }

    public double RamTotal { get; set; }

    public string SentAtUtc { get; set; } = string.Empty;
}
