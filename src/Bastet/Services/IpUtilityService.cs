using Bastet.Models;
using System.Net;

namespace Bastet.Services;

public class IpUtilityService : IIpUtilityService
{

    public string CalculateSubnetMask(int cidr)
    {
        if (cidr is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(cidr), "CIDR must be between 0 and 32");
        }

        uint mask = 0;
        if (cidr > 0)
        {

            mask = ~((1u << (32 - cidr)) - 1);
        }

        byte[] bytes = [(byte)(mask >> 24), (byte)(mask >> 16), (byte)(mask >> 8), (byte)mask];
        return new IPAddress(bytes).ToString();
    }

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

        if (cidr == 0)
        {
            return "255.255.255.255";
        }

        IPAddress network = IPAddress.Parse(networkAddress);
        byte[] networkBytes = network.GetAddressBytes();

        if (networkBytes.Length != 4)
        {
            throw new ArgumentException("Only IPv4 addresses are supported", nameof(networkAddress));
        }

        uint inverseMask = (1u << (32 - cidr)) - 1;

        uint networkInt = (uint)(networkBytes[0] << 24 |
                                networkBytes[1] << 16 |
                                networkBytes[2] << 8 |
                                networkBytes[3]);

        uint broadcastInt = networkInt | inverseMask;

        byte[] broadcastBytes =
        [
            (byte)(broadcastInt >> 24),
            (byte)(broadcastInt >> 16),
            (byte)(broadcastInt >> 8),
            (byte)broadcastInt,
        ];
        return new IPAddress(broadcastBytes).ToString();
    }

    public long CalculateTotalIpAddresses(int cidr) => cidr is < 0 or > 32
            ? throw new ArgumentOutOfRangeException(nameof(cidr), "CIDR must be between 0 and 32")
            : 1L << (32 - cidr);

    public long CalculateUsableIpAddresses(int cidr)
    {
        if (cidr is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(cidr), "CIDR must be between 0 and 32");
        }

        return UsableWithin(CalculateTotalIpAddresses(cidr));
    }

    private static long UsableWithin(long addressCount) =>
        addressCount <= 2 ? addressCount : addressCount - 2;

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

        if (cidr == 0 && networkAddress == "0.0.0.0")
        {
            return true;
        }

        try
        {
            IPAddress ip = IPAddress.Parse(networkAddress);
            byte[] addressBytes = ip.GetAddressBytes();

            if (addressBytes.Length != 4)
            {
                return false;
            }

            uint addressValue = BitConverter.ToUInt32([.. addressBytes.Reverse()], 0);

            int hostBits = 32 - cidr;

            uint hostBitMask = hostBits == 32 ? 0xFFFFFFFF : (1u << hostBits) - 1;

            return (addressValue & hostBitMask) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool IsSubnetContainedInParent(string childNetwork, int childCidr, string parentNetwork, int parentCidr)
    {
        if (string.IsNullOrEmpty(childNetwork) || string.IsNullOrEmpty(parentNetwork))
        {
            return false;
        }

        if (childCidr <= parentCidr)
        {
            return false;
        }

        try
        {
            IPAddress childIp = IPAddress.Parse(childNetwork);
            IPAddress parentIp = IPAddress.Parse(parentNetwork);

            byte[] childBytes = childIp.GetAddressBytes();
            byte[] parentBytes = parentIp.GetAddressBytes();

            if (childBytes.Length != 4 || parentBytes.Length != 4)
            {
                return false;
            }

            uint parentMask = (parentCidr == 0) ? 0 : ~((1u << (32 - parentCidr)) - 1);

            uint childNet = BitConverter.ToUInt32([.. childBytes.Reverse()], 0);
            uint parentNet = BitConverter.ToUInt32([.. parentBytes.Reverse()], 0);

            if (parentCidr == 0)
            {
                return true;
            }

            uint maskedChild = childNet & parentMask;
            uint maskedParent = parentNet & parentMask;

            return maskedChild == maskedParent;
        }
        catch
        {
            return false;
        }
    }

    public IEnumerable<IPRange> CalculateUnallocatedRanges(string networkAddress, int cidr, IEnumerable<Subnet> childSubnets, IEnumerable<HostIpAssignment> hostIpAssignments)
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

        if (networkBytes.Length != 4)
        {
            throw new ArgumentException("Only IPv4 addresses are supported", nameof(networkAddress));
        }

        List<IPRange> unallocatedRanges = [];

        uint startIp = BitConverter.ToUInt32([.. networkBytes.Reverse()], 0);
        long subnetSize = 1L << (32 - cidr);
        uint endIp = (uint)(startIp + subnetSize - 1);

        List<Subnet> validChildren = [.. childSubnets
            .Where(s => IsSubnetContainedInParent(s.NetworkAddress, s.Cidr, networkAddress, cidr))
            .OrderBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[0])
            .ThenBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[1])
            .ThenBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[2])
            .ThenBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[3])];

        List<(uint IpAddress, string IpString)> validHostIps = [];

        if (hostIpAssignments != null && hostIpAssignments.Any())
        {
            foreach (HostIpAssignment hostIp in hostIpAssignments)
            {
                if (IsIpInSubnet(hostIp.IP, networkAddress, cidr))
                {
                    IPAddress ipAddress = IPAddress.Parse(hostIp.IP);
                    byte[] ipBytes = ipAddress.GetAddressBytes();
                    uint ipInt = BitConverter.ToUInt32([.. ipBytes.Reverse()], 0);
                    validHostIps.Add((ipInt, hostIp.IP));
                }
            }
        }

        if (validChildren.Count == 0 && validHostIps.Count == 0)
        {
            unallocatedRanges.Add(new IPRange
            {
                StartIp = UIntToIpString(startIp),
                EndIp = UIntToIpString(endIp),
                AddressCount = subnetSize
            });

            StampUsable(unallocatedRanges);
            return unallocatedRanges;
        }

        List<(uint Start, uint End)> allocatedRanges = [];

        foreach (Subnet? child in validChildren)
        {
            byte[] childBytes = IPAddress.Parse(child.NetworkAddress).GetAddressBytes();
            uint childStart = BitConverter.ToUInt32([.. childBytes.Reverse()], 0);
            uint childSize = 1u << (32 - child.Cidr);
            uint childEnd = childStart + childSize - 1;

            allocatedRanges.Add((childStart, childEnd));
        }

        foreach ((uint IpAddress, _) in validHostIps)
        {
            allocatedRanges.Add((IpAddress, IpAddress));
        }

        allocatedRanges = [.. allocatedRanges.OrderBy(r => r.Start)];

        List<(uint Start, uint End)> mergedRanges = [];
        if (allocatedRanges.Count > 0)
        {
            (uint Start, uint End) current = allocatedRanges[0];
            for (int i = 1; i < allocatedRanges.Count; i++)
            {
                (uint Start, uint End) next = allocatedRanges[i];

                if (next.Start <= (long)current.End + 1)
                {
                    current.End = Math.Max(current.End, next.End);
                }
                else
                {

                    mergedRanges.Add(current);
                    current = next;
                }
            }

            mergedRanges.Add(current);
        }

        long currentPosition = startIp;

        foreach ((uint Start, uint End) in mergedRanges)
        {
            if (Start > currentPosition)
            {
                unallocatedRanges.Add(new IPRange
                {
                    StartIp = UIntToIpString((uint)currentPosition),
                    EndIp = UIntToIpString(Start - 1),
                    AddressCount = Start - currentPosition
                });
            }

            currentPosition = (long)End + 1;
        }

        if (currentPosition <= endIp)
        {
            unallocatedRanges.Add(new IPRange
            {
                StartIp = UIntToIpString((uint)currentPosition),
                EndIp = UIntToIpString(endIp),
                AddressCount = endIp - currentPosition + 1
            });
        }

        StampUsable(unallocatedRanges);
        return unallocatedRanges;
    }

    public IReadOnlyList<ChildSubnetSuggestion> SuggestChildSubnets(int parentCidr, IReadOnlyList<IPRange> unallocatedRanges)
    {
        if (parentCidr is < 0 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(parentCidr), "Parent CIDR must be between 0 and 31");
        }

        List<(long Start, long End)> ranges = [.. unallocatedRanges.Select(r => (ParseIpv4(r.StartIp), ParseIpv4(r.EndIp)))];
        List<ChildSubnetSuggestion> suggestions = [.. unallocatedRanges.Select(r => new ChildSubnetSuggestion { StartIp = r.StartIp })];
        int?[] recommended = new int?[ranges.Count];

        for (int cidr = parentCidr + 1; cidr <= 32; cidr++)
        {
            long size = 1L << (32 - cidr);
            long? lowestBlockAtOrAfter = null;

            for (int i = ranges.Count - 1; i >= 0; i--)
            {
                long aligned = AlignUp(ranges[i].Start, size);

                if (aligned + size - 1 <= ranges[i].End)
                {
                    lowestBlockAtOrAfter = aligned;

                    if (aligned == ranges[i].Start)
                    {
                        recommended[i] ??= cidr;
                    }
                }

                suggestions[i].NetworkAddressByCidr[cidr] = lowestBlockAtOrAfter is long block ? UIntToIpString((uint)block) : null;
            }
        }

        for (int i = 0; i < suggestions.Count; i++)
        {
            suggestions[i].RecommendedCidr = recommended[i]
                ?? throw new ArgumentException($"Range {unallocatedRanges[i].StartIp}-{unallocatedRanges[i].EndIp} holds no address", nameof(unallocatedRanges));
        }

        return suggestions;
    }

    public bool IsIpInSubnet(string ip, string networkAddress, int cidr)
    {
        if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(networkAddress))
        {
            return false;
        }

        if (cidr is < 0 or > 32)
        {
            return false;
        }

        try
        {

            IPAddress ipAddress = IPAddress.Parse(ip);
            IPAddress networkIpAddress = IPAddress.Parse(networkAddress);

            byte[] ipBytes = ipAddress.GetAddressBytes();
            byte[] networkBytes = networkIpAddress.GetAddressBytes();

            if (ipBytes.Length != 4 || networkBytes.Length != 4)
            {
                return false;
            }

            uint ipValue = BitConverter.ToUInt32([.. ipBytes.Reverse()], 0);
            uint networkValue = BitConverter.ToUInt32([.. networkBytes.Reverse()], 0);

            uint mask = (cidr == 0) ? 0 : ~((1u << (32 - cidr)) - 1);

            return (ipValue & mask) == (networkValue & mask);
        }
        catch
        {
            return false;
        }
    }

    #region Helper Methods

    private static string UIntToIpString(uint ipInt)
    {
        byte[] bytes = [(byte)(ipInt >> 24), (byte)(ipInt >> 16), (byte)(ipInt >> 8), (byte)ipInt];
        return new IPAddress(bytes).ToString();
    }

    private static long ParseIpv4(string ip)
    {
        byte[] bytes = IPAddress.Parse(ip).GetAddressBytes();

        if (bytes.Length != 4)
        {
            throw new ArgumentException("Only IPv4 addresses are supported", nameof(ip));
        }

        return BitConverter.ToUInt32([.. bytes.Reverse()], 0);
    }

    private static long AlignUp(long value, long size) => (value + size - 1) / size * size;

    #endregion

    private static void StampUsable(List<IPRange> ranges)
    {
        foreach (IPRange r in ranges)
        {
            r.UsableCount = UsableWithin(r.AddressCount);
        }
    }
}
