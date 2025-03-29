namespace Bastet.Models;

/// <summary>
/// Represents subnet calculation results
/// </summary>
public class SubnetCalculation
{
    public string NetworkAddress { get; set; } = string.Empty;
    public int Cidr { get; set; }
    public string SubnetMask { get; set; } = string.Empty;
    public string BroadcastAddress { get; set; } = string.Empty;
    public long TotalIpAddresses { get; set; }
    public long UsableIpAddresses { get; set; }
}

/// <summary>
/// Represents a range of IP addresses
/// </summary>
public class IPRange
{
    public string StartIp { get; set; } = string.Empty;
    public string EndIp { get; set; } = string.Empty;
    public long AddressCount { get; set; }
}
