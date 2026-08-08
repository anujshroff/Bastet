using System.Net;
using Bastet.Models;
using Bastet.Services;

namespace Bastet.Tests.Services;

public class IpUtilityServiceTests
{
    private readonly IpUtilityService _svc = new();

    [Fact]
    public void CalculateUnallocatedRanges_SlashZero_Empty_ReturnsWholeIpv4Space()
    {
        List<IPRange> ranges = [.. _svc.CalculateUnallocatedRanges("0.0.0.0", 0, [], [])];

        IPRange range = Assert.Single(ranges);
        Assert.Equal("0.0.0.0", range.StartIp);
        Assert.Equal("255.255.255.255", range.EndIp);
        Assert.Equal(4294967294L, range.AddressCount);
    }

    [Fact]
    public void CalculateUnallocatedRanges_SlashZero_WithChild_GapsAreBoundedByTheWholeSpace()
    {
        Subnet child = new() { NetworkAddress = "10.0.0.0", Cidr = 8 };

        List<IPRange> ranges = [.. _svc.CalculateUnallocatedRanges("0.0.0.0", 0, [child], [])];

        Assert.NotEmpty(ranges);
        Assert.Equal("0.0.0.0", ranges.First().StartIp);

        Assert.Equal("255.255.255.254", ranges.Last().EndIp);
    }

    [Fact]
    public void CalculateUnallocatedRanges_AllocationEndingAtTopOfAddressSpace_ReportsOnlyTheRealGap()
    {

        Subnet child = new() { NetworkAddress = "255.255.255.128", Cidr = 25 };

        List<IPRange> ranges = [.. _svc.CalculateUnallocatedRanges("255.255.255.0", 24, [child], [])];

        IPRange range = Assert.Single(ranges);
        Assert.Equal("255.255.255.0", range.StartIp);
        Assert.Equal("255.255.255.127", range.EndIp);
    }

    [Fact]
    public void CalculateUnallocatedRanges_SlashZero_WithChildAtTopOfAddressSpace_ReportsOnlyTheRealGap()
    {

        Subnet child = new() { NetworkAddress = "255.0.0.0", Cidr = 8 };

        List<IPRange> ranges = [.. _svc.CalculateUnallocatedRanges("0.0.0.0", 0, [child], [])];

        IPRange range = Assert.Single(ranges);
        Assert.Equal("0.0.0.0", range.StartIp);
        Assert.Equal("254.255.255.255", range.EndIp);
    }

    [Fact]
    public void CalculateUnallocatedRanges_ANestedRowUnderAnAllocationEndingAtTheTopOfTheSpace_DoesNotOpenTheAllocationUp()
    {
        List<Subnet> children =
        [
            new() { NetworkAddress = "255.255.254.0", Cidr = 24 },
            new() { NetworkAddress = "255.255.255.0", Cidr = 24 },
            new() { NetworkAddress = "255.255.255.0", Cidr = 26 }
        ];

        Assert.Empty(_svc.CalculateUnallocatedRanges("255.255.254.0", 23, children, []));
    }

    [Fact]
    public void CalculateUnallocatedRanges_NeverReturnsSpaceInsideAnAllocationItWasGiven()
    {
        Random rng = new(20260808);
        int[] parentCidrs = [0, 1, 2, 3, 7, 8, 15, 16, 22, 23, 24, 29, 30];

        for (int iteration = 0; iteration < 4000; iteration++)
        {
            int parentCidr = parentCidrs[rng.Next(parentCidrs.Length)];
            ulong parentSize = 1UL << (32 - parentCidr);
            uint parentStart = iteration % 2 == 0
                ? (uint)(0x1_0000_0000UL - parentSize)
                : (uint)((ulong)rng.NextInt64(0, (long)(0x1_0000_0000UL / parentSize)) * parentSize);

            List<(uint Start, uint End)> allocations = [];
            List<Subnet> children = [];

            for (int c = 0; c < rng.Next(1, 5); c++)
            {
                int childCidr = Math.Min(32, parentCidr + rng.Next(1, 9));
                ulong childSize = 1UL << (32 - childCidr);
                ulong blocks = parentSize / childSize;
                ulong offset = (c == 0 && iteration % 2 == 0)
                    ? parentSize - childSize
                    : (ulong)rng.NextInt64(0, (long)blocks) * childSize;

                uint childStart = parentStart + (uint)offset;
                children.Add(new Subnet { NetworkAddress = ToIp(childStart), Cidr = childCidr });
                allocations.Add((childStart, (uint)(childStart + childSize - 1)));
            }

            foreach (IPRange range in _svc.CalculateUnallocatedRanges(ToIp(parentStart), parentCidr, children, []))
            {
                uint rangeStart = ToUInt(range.StartIp);
                uint rangeEnd = ToUInt(range.EndIp);

                Assert.DoesNotContain(allocations, a => rangeStart <= a.End && a.Start <= rangeEnd);
            }
        }
    }

    private static string ToIp(uint value) =>
        new IPAddress(BitConverter.GetBytes(value).Reverse().ToArray()).ToString();

    private static uint ToUInt(string ip) =>
        BitConverter.ToUInt32([.. IPAddress.Parse(ip).GetAddressBytes().Reverse()], 0);

    [Fact]
    public void CalculateUnallocatedRanges_MidSpaceSubnet_IsUnaffected()
    {

        Subnet child = new() { NetworkAddress = "10.0.0.64", Cidr = 26 };

        List<IPRange> ranges = [.. _svc.CalculateUnallocatedRanges("10.0.0.0", 24, [child], [])];

        Assert.Equal(2, ranges.Count);
        Assert.Equal("10.0.0.0", ranges[0].StartIp);
        Assert.Equal("10.0.0.63", ranges[0].EndIp);
        Assert.Equal("10.0.0.128", ranges[1].StartIp);
        Assert.Equal("10.0.0.254", ranges[1].EndIp);
    }
}
