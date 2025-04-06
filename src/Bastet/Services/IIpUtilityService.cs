using Bastet.Models;

namespace Bastet.Services;

/// <summary>
/// Service interface for IP subnet calculations
/// </summary>
public interface IIpUtilityService
{
    /// <summary>
    /// Calculates the subnet mask from a CIDR value
    /// </summary>
    string CalculateSubnetMask(int cidr);

    /// <summary>
    /// Calculates the broadcast address for a given network and CIDR
    /// </summary>
    string CalculateBroadcastAddress(string networkAddress, int cidr);

    /// <summary>
    /// Calculates the total number of IP addresses in a subnet
    /// </summary>
    long CalculateTotalIpAddresses(int cidr);

    /// <summary>
    /// Calculates the number of usable IP addresses in a subnet
    /// </summary>
    long CalculateUsableIpAddresses(int cidr);

    /// <summary>
    /// Verifies if a subnet is valid (network address aligns with CIDR)
    /// </summary>
    bool IsValidSubnet(string networkAddress, int cidr);

    /// <summary>
    /// Checks if a subnet is contained within a parent subnet
    /// </summary>
    bool IsSubnetContainedInParent(string childNetwork, int childCidr, string parentNetwork, int parentCidr);
    
    /// <summary>
    /// Checks if an IP address is within a subnet's range
    /// </summary>
    bool IsIpInSubnet(string ip, string networkAddress, int cidr);

    /// <summary>
    /// Calculates possible subnets when dividing a network with a specific CIDR into smaller subnets
    /// </summary>
    IEnumerable<SubnetCalculation> CalculatePossibleSubnets(string networkAddress, int currentCidr, int targetCidr);

    /// <summary>
    /// Calculates unallocated IP ranges within a subnet, taking into account child subnets
    /// </summary>
    IEnumerable<IPRange> CalculateUnallocatedRanges(string networkAddress, int cidr, IEnumerable<Subnet> childSubnets);
}
