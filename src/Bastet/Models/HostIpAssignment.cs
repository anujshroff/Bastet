using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net;

namespace Bastet.Models;

/// <summary>
/// Represents a host IP assignment within a subnet
/// </summary>
public class HostIpAssignment : BaseEntity
{
    [Key]
    [MaxLength(15)] // IPv4 addresses are max 15 characters
    public string IP { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Name { get; set; }

    // Foreign key relationship
    public int SubnetId { get; set; }

    [ForeignKey(nameof(SubnetId))]
    public Subnet Subnet { get; set; } = null!;

    // Concurrency control
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// Determines if this IP address is within the assigned subnet
    /// </summary>
    /// <returns>True if IP is within subnet range, false otherwise</returns>
    public bool IsWithinSubnet()
    {
        try
        {
            // Parse IP addresses
            if (!IPAddress.TryParse(IP, out IPAddress? ipAddress) ||
                !IPAddress.TryParse(Subnet.NetworkAddress, out IPAddress? networkAddress))
            {
                return false;
            }

            byte[] ipBytes = ipAddress.GetAddressBytes();
            byte[] networkBytes = networkAddress.GetAddressBytes();

            // Verify both are IPv4
            if (ipBytes.Length != 4 || networkBytes.Length != 4)
            {
                return false;
            }

            // Convert to UInt32 for bitwise operations
            uint ipValue = BitConverter.ToUInt32([.. ipBytes.Reverse()], 0);
            uint networkValue = BitConverter.ToUInt32([.. networkBytes.Reverse()], 0);

            // Create subnet mask
            uint mask = (Subnet.Cidr == 0) ? 0 : ~((1u << (32 - Subnet.Cidr)) - 1);

            // Check if IP is in subnet
            return (ipValue & mask) == (networkValue & mask);
        }
        catch
        {
            return false;
        }
    }
}
