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
        Assert.Equal(4294967294L, range.AddressCount); // 2^32 - 2 usable
    }

    [Fact]
    public void CalculateUnallocatedRanges_SlashZero_WithChild_GapsAreBoundedByTheWholeSpace()
    {
        Subnet child = new() { NetworkAddress = "10.0.0.0", Cidr = 8 };

        List<IPRange> ranges = [.. _svc.CalculateUnallocatedRanges("0.0.0.0", 0, [child], [])];

        Assert.NotEmpty(ranges);
        Assert.Equal("0.0.0.0", ranges.First().StartIp);
        // Spans the whole space; the broadcast address is excluded for a cidr < 31 subnet.
        Assert.Equal("255.255.255.254", ranges.Last().EndIp);
    }

    [Fact]
    public void CalculateUnallocatedRanges_AllocationEndingAtTopOfAddressSpace_ReportsOnlyTheRealGap()
    {
        // The child takes the upper half of 255.255.255.0/24, so the allocation ends at
        // 255.255.255.255. Advancing past it overflowed a uint cursor back to 0, which read as
        // "space left" and produced a second, phantom range covering the whole address space.
        Subnet child = new() { NetworkAddress = "255.255.255.128", Cidr = 25 };

        List<IPRange> ranges = [.. _svc.CalculateUnallocatedRanges("255.255.255.0", 24, [child], [])];

        IPRange range = Assert.Single(ranges);
        Assert.Equal("255.255.255.0", range.StartIp);
        Assert.Equal("255.255.255.127", range.EndIp);
    }

    [Fact]
    public void CalculateUnallocatedRanges_SlashZero_WithChildAtTopOfAddressSpace_ReportsOnlyTheRealGap()
    {
        // Same overflow reached from the other extreme: the entire IPv4 space with its final /8 taken.
        Subnet child = new() { NetworkAddress = "255.0.0.0", Cidr = 8 };

        List<IPRange> ranges = [.. _svc.CalculateUnallocatedRanges("0.0.0.0", 0, [child], [])];

        IPRange range = Assert.Single(ranges);
        Assert.Equal("0.0.0.0", range.StartIp);
        Assert.Equal("254.255.255.255", range.EndIp);
    }

    [Fact]
    public void CalculateUnallocatedRanges_MidSpaceSubnet_IsUnaffected()
    {
        // An ordinary subnet keeps both gaps around its child, with the broadcast address excluded.
        Subnet child = new() { NetworkAddress = "10.0.0.64", Cidr = 26 };

        List<IPRange> ranges = [.. _svc.CalculateUnallocatedRanges("10.0.0.0", 24, [child], [])];

        Assert.Equal(2, ranges.Count);
        Assert.Equal("10.0.0.0", ranges[0].StartIp);
        Assert.Equal("10.0.0.63", ranges[0].EndIp);
        Assert.Equal("10.0.0.128", ranges[1].StartIp);
        Assert.Equal("10.0.0.254", ranges[1].EndIp);
    }
}
