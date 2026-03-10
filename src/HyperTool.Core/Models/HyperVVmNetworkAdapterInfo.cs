namespace HyperTool.Models;

public sealed class HyperVVmNetworkAdapterInfo
{
    public string Name { get; set; } = string.Empty;

    public string SwitchName { get; set; } = string.Empty;

    public string MacAddress { get; set; } = string.Empty;

    public List<string> IpAddresses { get; set; } = [];

    public string Ipv4Address { get; set; } = string.Empty;

    public string Ipv4SubnetMask { get; set; } = string.Empty;

    public string Ipv4Gateway { get; set; } = string.Empty;

    public string GuestComputerName { get; set; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Network Adapter" : Name;

    public string GuestNetworkDisplay
    {
        get
        {
            var ip = string.IsNullOrWhiteSpace(Ipv4Address) ? "-" : Ipv4Address;
            var mask = string.IsNullOrWhiteSpace(Ipv4SubnetMask) ? "-" : Ipv4SubnetMask;
            var gateway = string.IsNullOrWhiteSpace(Ipv4Gateway) ? "-" : Ipv4Gateway;
            var guest = string.IsNullOrWhiteSpace(GuestComputerName) ? string.Empty : $"  [Guest: {GuestComputerName}]";
            return $"IPv4 {ip}  |  Maske {mask}  |  Standardgateway {gateway}{guest}";
        }
    }

    public override string ToString() => DisplayName;
}