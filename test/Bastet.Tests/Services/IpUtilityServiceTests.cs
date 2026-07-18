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
}
