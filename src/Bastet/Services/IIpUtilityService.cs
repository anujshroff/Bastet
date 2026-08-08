using Bastet.Models;

namespace Bastet.Services;

public interface IIpUtilityService
{

    string CalculateSubnetMask(int cidr);

    string CalculateBroadcastAddress(string networkAddress, int cidr);

    long CalculateTotalIpAddresses(int cidr);

    long CalculateUsableIpAddresses(int cidr);

    bool IsValidSubnet(string networkAddress, int cidr);

    bool IsSubnetContainedInParent(string childNetwork, int childCidr, string parentNetwork, int parentCidr);

    bool IsIpInSubnet(string ip, string networkAddress, int cidr);

    IEnumerable<IPRange> CalculateUnallocatedRanges(string networkAddress, int cidr, IEnumerable<Subnet> childSubnets);

    IEnumerable<IPRange> CalculateUnallocatedRanges(string networkAddress, int cidr, IEnumerable<Subnet> childSubnets, IEnumerable<HostIpAssignment> hostIpAssignments);
}
