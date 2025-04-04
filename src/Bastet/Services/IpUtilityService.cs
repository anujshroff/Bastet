using Bastet.Models;
using System.Net;

namespace Bastet.Services;

/// <summary>
/// Service for IP subnet calculations and utilities
/// </summary>
public class IpUtilityService : IIpUtilityService
{
    /// <summary>
    /// Calculates the subnet mask from a CIDR value
    /// </summary>
    public string CalculateSubnetMask(int cidr)
    {
        if (cidr is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(cidr), "CIDR must be between 0 and 32");
        }

        uint mask = 0;
        if (cidr > 0)
        {
            // Create a bit mask based on the CIDR
            // For example, CIDR 24 = 11111111.11111111.11111111.00000000 = 255.255.255.0
            mask = ~((1u << (32 - cidr)) - 1);
        }

        // Convert to IP format
        byte[] bytes = [(byte)(mask >> 24), (byte)(mask >> 16), (byte)(mask >> 8), (byte)mask];
        return new IPAddress(bytes).ToString();
    }

    /// <summary>
    /// Calculates the broadcast address for a given network and CIDR
    /// </summary>
    public string CalculateBroadcastAddress(string networkAddress, int cidr)
    {
        if (string.IsNullOrEmpty(networkAddress))
        {
            throw new ArgumentNullException(nameof(networkAddress));
        }

        if (cidr is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(cidr), "CIDR must be between 0 and 32");
        }

        // Special case for CIDR 0 (entire internet)
        if (cidr == 0)
        {
            return "255.255.255.255";
        }

        // Parse the network address
        IPAddress network = IPAddress.Parse(networkAddress);
        byte[] networkBytes = network.GetAddressBytes();

        // Verify this is an IPv4 address
        if (networkBytes.Length != 4)
        {
            throw new ArgumentException("Only IPv4 addresses are supported", nameof(networkAddress));
        }

        // Create inverse of the subnet mask
        uint inverseMask = (1u << (32 - cidr)) - 1;

        // Get the network address as an unsigned integer
        uint networkInt = (uint)(networkBytes[0] << 24 |
                                networkBytes[1] << 16 |
                                networkBytes[2] << 8 |
                                networkBytes[3]);

        // Calculate broadcast by OR'ing the network with inverse mask
        uint broadcastInt = networkInt | inverseMask;

        // Convert back to IP address
        byte[] broadcastBytes =
        [
            (byte)(broadcastInt >> 24),
            (byte)(broadcastInt >> 16),
            (byte)(broadcastInt >> 8),
            (byte)broadcastInt,
        ];
        return new IPAddress(broadcastBytes).ToString();
    }

    /// <summary>
    /// Calculates the total number of IP addresses in a subnet
    /// </summary>
    public long CalculateTotalIpAddresses(int cidr) => cidr is < 0 or > 32
            ? throw new ArgumentOutOfRangeException(nameof(cidr), "CIDR must be between 0 and 32")
            : (long)Math.Pow(2, 32 - cidr);

    /// <summary>
    /// Calculates the number of usable IP addresses in a subnet
    /// </summary>
    public long CalculateUsableIpAddresses(int cidr)
    {
        if (cidr is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(cidr), "CIDR must be between 0 and 32");
        }

        if (cidr >= 31)
        {
            // Special cases: /31 allows 2 usable addresses (RFC 3021)
            // /32 is a single host
            return cidr == 31 ? 2 : 1;
        }

        return Math.Max(0, (long)Math.Pow(2, 32 - cidr) - 2);
    }

    /// <summary>
    /// Verifies if a subnet is valid (network address aligns with CIDR)
    /// </summary>
    public bool IsValidSubnet(string networkAddress, int cidr)
    {
        if (string.IsNullOrEmpty(networkAddress))
        {
            return false;
        }

        if (cidr is < 0 or > 32)
        {
            return false;
        }

        // Special case for CIDR 0 (entire internet)
        if (cidr == 0 && networkAddress == "0.0.0.0")
        {
            return true;
        }

        try
        {
            IPAddress ip = IPAddress.Parse(networkAddress);
            byte[] addressBytes = ip.GetAddressBytes();

            // Verify this is an IPv4 address
            if (addressBytes.Length != 4)
            {
                return false;
            }

            // Convert to one 32-bit number
            uint addressValue = BitConverter.ToUInt32([.. addressBytes.Reverse()], 0);

            // Calculate number of host bits
            int hostBits = 32 - cidr;

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
    /// Checks if a subnet is contained within a parent subnet
    /// </summary>
    public bool IsSubnetContainedInParent(string childNetwork, int childCidr, string parentNetwork, int parentCidr)
    {
        if (string.IsNullOrEmpty(childNetwork) || string.IsNullOrEmpty(parentNetwork))
        {
            return false;
        }

        // Child CIDR must be larger than parent CIDR (smaller network)
        if (childCidr <= parentCidr)
        {
            return false;
        }

        try
        {
            IPAddress childIp = IPAddress.Parse(childNetwork);
            IPAddress parentIp = IPAddress.Parse(parentNetwork);

            // Both must be IPv4 addresses
            byte[] childBytes = childIp.GetAddressBytes();
            byte[] parentBytes = parentIp.GetAddressBytes();

            if (childBytes.Length != 4 || parentBytes.Length != 4)
            {
                return false;
            }

            // Create a subnet mask for the parent
            uint parentMask = (parentCidr == 0) ? 0 : ~((1u << (32 - parentCidr)) - 1);

            // Get the network addresses as unsigned integers
            uint childNet = BitConverter.ToUInt32([.. childBytes.Reverse()], 0);
            uint parentNet = BitConverter.ToUInt32([.. parentBytes.Reverse()], 0);

            // For CIDR 0 (entire internet), any child subnet is contained within it
            if (parentCidr == 0)
            {
                return true;
            }

            // Apply the parent subnet mask to both addresses
            uint maskedChild = childNet & parentMask;
            uint maskedParent = parentNet & parentMask;

            // If they match, child is within parent
            // Example: 10.0.0.0/16 contains 10.0.1.0/24 but not 10.1.0.0/24
            return maskedChild == maskedParent;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Calculates possible subnets when dividing a network with a specific CIDR into smaller subnets
    /// </summary>
    public IEnumerable<SubnetCalculation> CalculatePossibleSubnets(string networkAddress, int currentCidr, int targetCidr)
    {
        if (string.IsNullOrEmpty(networkAddress))
        {
            throw new ArgumentNullException(nameof(networkAddress));
        }

        if (currentCidr is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(currentCidr), "CIDR must be between 0 and 32");
        }

        if (targetCidr is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(targetCidr), "CIDR must be between 0 and 32");
        }

        // Target CIDR must be larger than current CIDR for subnetting
        if (targetCidr <= currentCidr)
        {
            throw new ArgumentException("Target CIDR must be larger than current CIDR", nameof(targetCidr));
        }

        List<SubnetCalculation> results = [];
        IPAddress network = IPAddress.Parse(networkAddress);
        byte[] networkBytes = network.GetAddressBytes();

        // Verify this is an IPv4 address
        if (networkBytes.Length != 4)
        {
            throw new ArgumentException("Only IPv4 addresses are supported", nameof(networkAddress));
        }

        // Calculate the number of subnets
        int subnetCount = 1 << (targetCidr - currentCidr);

        // Convert network address to integer for easier calculation
        uint networkInt = BitConverter.ToUInt32([.. networkBytes.Reverse()], 0);

        // Calculate the size of each subnet
        uint subnetSize = 1u << (32 - targetCidr);

        // Generate all possible subnets
        for (int i = 0 ; i < subnetCount ; i++)
        {
            uint newNetworkInt = networkInt + (subnetSize * (uint)i);

            // Convert to byte array
            byte[] newNetworkBytes = [.. BitConverter.GetBytes(newNetworkInt).Reverse()];
            string newNetworkAddress = new IPAddress(newNetworkBytes).ToString();

            results.Add(new SubnetCalculation
            {
                NetworkAddress = newNetworkAddress,
                Cidr = targetCidr,
                SubnetMask = CalculateSubnetMask(targetCidr),
                BroadcastAddress = CalculateBroadcastAddress(newNetworkAddress, targetCidr),
                TotalIpAddresses = CalculateTotalIpAddresses(targetCidr),
                UsableIpAddresses = CalculateUsableIpAddresses(targetCidr)
            });
        }

        return results;
    }

    /// <summary>
    /// Calculates unallocated IP ranges within a subnet, taking into account child subnets
    /// </summary>
    public IEnumerable<IPRange> CalculateUnallocatedRanges(string networkAddress, int cidr, IEnumerable<Subnet> childSubnets)
    {
        if (string.IsNullOrEmpty(networkAddress))
        {
            throw new ArgumentNullException(nameof(networkAddress));
        }

        if (cidr is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(cidr), "CIDR must be between 0 and 32");
        }

        IPAddress network = IPAddress.Parse(networkAddress);
        byte[] networkBytes = network.GetAddressBytes();

        // Verify this is an IPv4 address
        if (networkBytes.Length != 4)
        {
            throw new ArgumentException("Only IPv4 addresses are supported", nameof(networkAddress));
        }

        List<IPRange> unallocatedRanges = [];

        // Get the subnet range
        uint startIp = BitConverter.ToUInt32([.. networkBytes.Reverse()], 0);
        uint subnetSize = 1u << (32 - cidr);
        uint endIp = startIp + subnetSize - 1;

        // Get valid child subnets
        List<Subnet> validChildren = [.. childSubnets
            .Where(s => IsSubnetContainedInParent(s.NetworkAddress, s.Cidr, networkAddress, cidr))
            .OrderBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[0])
            .ThenBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[1])
            .ThenBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[2])
            .ThenBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[3])];

        if (validChildren.Count == 0)
        {
            // No children, entire subnet is unallocated
            // For /31 or /32, the entire range is usable according to RFC 3021
            if (cidr >= 31)
            {
                unallocatedRanges.Add(new IPRange
                {
                    StartIp = UIntToIpString(startIp),
                    EndIp = UIntToIpString(endIp),
                    AddressCount = subnetSize
                });
            }
            else
            {
                // For normal subnets, always start with the network address for subnet creation
                // purposes, but display the correct usable address count
                unallocatedRanges.Add(new IPRange
                {
                    StartIp = UIntToIpString(startIp), // Return the network address for subnetting
                    EndIp = UIntToIpString(endIp),     // Return the broadcast address for range display
                    AddressCount = subnetSize - 2      // But still show correct usable IP count
                });
            }

            return unallocatedRanges;
        }

        // Sort child subnets by start address
        List<(uint Start, uint End)> allocatedRanges = [];

        foreach (Subnet? child in validChildren)
        {
            byte[] childBytes = IPAddress.Parse(child.NetworkAddress).GetAddressBytes();
            uint childStart = BitConverter.ToUInt32([.. childBytes.Reverse()], 0);
            uint childSize = 1u << (32 - child.Cidr);
            uint childEnd = childStart + childSize - 1;

            allocatedRanges.Add((childStart, childEnd));
        }

        // Sort by start address
        allocatedRanges = [.. allocatedRanges.OrderBy(r => r.Start)];

        // Find gaps between allocated ranges
        uint currentPosition = startIp;

        // Don't skip network address to allow proper subnet creation
        // This ensures the network address is included in the range

        foreach ((uint Start, uint End) in allocatedRanges)
        {
            if (Start > currentPosition)
            {
                // Found a gap
                // For the first gap that starts at the network address, adjust the address count to show usable IPs
                if (currentPosition == startIp && cidr < 31)
                {
                    unallocatedRanges.Add(new IPRange
                    {
                        StartIp = UIntToIpString(currentPosition),
                        EndIp = UIntToIpString(Start - 1),
                        AddressCount = (Start - currentPosition > 2) ? Start - currentPosition - 1 : Start - currentPosition
                    });
                }
                else
                {
                    unallocatedRanges.Add(new IPRange
                    {
                        StartIp = UIntToIpString(currentPosition),
                        EndIp = UIntToIpString(Start - 1),
                        AddressCount = Start - currentPosition
                    });
                }
            }

            // Move position to after this allocated range
            currentPosition = End + 1;
        }

        // Check if there's space after the last allocated range
        if (currentPosition < endIp || (currentPosition == endIp && cidr >= 31))
        {
            uint lastIp = endIp;

            // Exclude broadcast address if not /31 or /32
            if (cidr < 31)
            {
                lastIp--;
            }

            if (currentPosition <= lastIp)
            {
                // For the last range that ends at the broadcast address, calculate the address count correctly
                if (lastIp == endIp - 1 && cidr < 31)
                {
                    unallocatedRanges.Add(new IPRange
                    {
                        StartIp = UIntToIpString(currentPosition),
                        EndIp = UIntToIpString(lastIp),
                        AddressCount = (lastIp - currentPosition > 0) ? lastIp - currentPosition : 1
                    });
                }
                else
                {
                    unallocatedRanges.Add(new IPRange
                    {
                        StartIp = UIntToIpString(currentPosition),
                        EndIp = UIntToIpString(lastIp),
                        AddressCount = lastIp - currentPosition + 1
                    });
                }
            }
        }

        return unallocatedRanges;
    }

    #region Helper Methods

    /// <summary>
    /// Converts a uint to an IPv4 address string
    /// </summary>
    private static string UIntToIpString(uint ipInt)
    {
        byte[] bytes = [(byte)(ipInt >> 24), (byte)(ipInt >> 16), (byte)(ipInt >> 8), (byte)ipInt];
        return new IPAddress(bytes).ToString();
    }

    #endregion
}
