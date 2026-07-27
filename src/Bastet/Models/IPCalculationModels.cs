namespace Bastet.Models;

/// <summary>
/// Represents a range of IP addresses
/// </summary>
public class IPRange
{
    public string StartIp { get; set; } = string.Empty;
    public string EndIp { get; set; } = string.Empty;
    public long AddressCount { get; set; }
}
