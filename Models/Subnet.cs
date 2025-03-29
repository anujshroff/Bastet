using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net;

namespace Bastet.Models;

/// <summary>
/// Represents a subnet in the BASTET system
/// </summary>
public class Subnet : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(15)] // IPv4 addresses are max 15 characters
    public string NetworkAddress { get; set; } = string.Empty;

    [Required]
    [Range(0, 32)] // IPv4 supports up to /32
    public int Cidr { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(255)]
    public string? Tags { get; set; }

    // Parent-Child Relationship
    public int? ParentSubnetId { get; set; }

    [ForeignKey(nameof(ParentSubnetId))]
    public Subnet? ParentSubnet { get; set; }

    public ICollection<Subnet> ChildSubnets { get; set; } = [];

    // Concurrency control
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    // Domain validation methods

    /// <summary>
    /// Determines if the network address is valid for the CIDR and IP version
    /// </summary>
    /// <returns>True if the network is valid, false otherwise</returns>
    public bool IsValidNetwork()
    {
        try
        {
            // Check if we can parse the IP address
            if (!IPAddress.TryParse(NetworkAddress, out IPAddress? ipAddress))
            {
                return false;
            }

            byte[] addressBytes = ipAddress.GetAddressBytes();

            // Verify IP address is IPv4
            if (addressBytes.Length != 4)
            {
                return false;
            }

            // Validate CIDR range
            if (Cidr > 32)
            {
                return false;
            }

            // Convert to one 32-bit number
            uint addressValue = BitConverter.ToUInt32([.. addressBytes.Reverse()], 0);

            // Calculate number of host bits
            int hostBits = 32 - Cidr;

            // Check if any host bits are set (which would make it invalid)
            uint hostBitMask = hostBits == 32 ? 0xFFFFFFFF : (1u << hostBits) - 1;

            return (addressValue & hostBitMask) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Determines if this subnet can contain the specified child subnet
    /// </summary>
    /// <param name="childSubnet">The child subnet to check</param>
    /// <returns>True if this subnet can contain the child subnet</returns>
    public bool CanContainSubnet(Subnet childSubnet)
    {
        // Child CIDR must be larger than parent CIDR (smaller subnet)
        if (childSubnet.Cidr <= Cidr)
        {
            return false;
        }

        try
        {
            IPAddress childIp = IPAddress.Parse(childSubnet.NetworkAddress);
            IPAddress parentIp = IPAddress.Parse(NetworkAddress);

            byte[] childBytes = childIp.GetAddressBytes();
            byte[] parentBytes = parentIp.GetAddressBytes();

            // Create a subnet mask for the parent
            uint parentMask = (Cidr == 0) ? 0 : ~((1u << (32 - Cidr)) - 1);

            // Get the network addresses as unsigned integers
            uint childNet = BitConverter.ToUInt32([.. childBytes.Reverse()], 0);
            uint parentNet = BitConverter.ToUInt32([.. parentBytes.Reverse()], 0);

            // Apply the parent subnet mask to both addresses
            uint maskedChild = childNet & parentMask;
            uint maskedParent = parentNet & parentMask;

            // If they match, child is within parent
            return maskedChild == maskedParent;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Determines if this subnet overlaps with another subnet
    /// </summary>
    /// <param name="otherSubnet">The subnet to check for overlap</param>
    /// <returns>True if there is an overlap</returns>
    public bool OverlapsWith(Subnet otherSubnet) =>
        // If either subnet contains the other, they overlap
        CanContainSubnet(otherSubnet) || otherSubnet.CanContainSubnet(this);

    /// <summary>
    /// Determines if this subnet can be deleted
    /// </summary>
    /// <returns>True if the subnet can be deleted</returns>
    public bool CanBeDeleted() =>
        // Can only delete if there are no child subnets
        ChildSubnets.Count == 0;
}
