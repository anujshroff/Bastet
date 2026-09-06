using Bastet.Models;
using Bastet.Services;
using System.Net;

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
        Assert.Equal(4294967296L, range.AddressCount);
        Assert.Equal(4294967294L, range.UsableCount);
    }

    [Fact]
    public void CalculateUnallocatedRanges_SlashZero_WithChild_GapsAreBoundedByTheWholeSpace()
    {
        Subnet child = new() { NetworkAddress = "10.0.0.0", Cidr = 8 };

        List<IPRange> ranges = [.. _svc.CalculateUnallocatedRanges("0.0.0.0", 0, [child], [])];

        Assert.NotEmpty(ranges);
        Assert.Equal("0.0.0.0", ranges.First().StartIp);

        Assert.Equal("255.255.255.255", ranges.Last().EndIp);
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
        new IPAddress([.. BitConverter.GetBytes(value).Reverse()]).ToString();

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
        Assert.Equal("10.0.0.255", ranges[1].EndIp);
    }

    public static TheoryData<string, int, string[]> SpanCases => new()
    {
        { "10.0.0.0", 24, [] },
        { "10.0.0.0", 30, [] },
        { "10.0.0.0", 31, [] },
        { "10.0.0.0", 32, [] },
        { "10.0.0.0", 24, ["10.0.0.64/26"] },
        { "10.0.0.0", 24, ["10.0.0.0/25"] },
        { "10.0.0.0", 24, ["10.0.0.128/25"] },
        { "10.0.0.0", 24, ["10.0.0.64/26", "10.0.0.192/26"] },
        { "10.20.0.0", 16, ["10.20.4.0/22"] },
        { "192.168.5.0", 28, ["192.168.5.4/30"] },
        { "0.0.0.0", 0, ["64.0.0.0/2"] },
    };

    [Theory]
    [MemberData(nameof(SpanCases))]
    public void EveryRange_CountsTheAddressesItActuallySpans(string network, int cidr, string[] kids)
    {
        List<Subnet> children = [.. kids.Select(k => new Subnet
        {
            NetworkAddress = k.Split('/')[0],
            Cidr = int.Parse(k.Split('/')[1])
        })];

        foreach (IPRange r in _svc.CalculateUnallocatedRanges(network, cidr, children, []))
        {
            long span = ToUint(r.EndIp) - ToUint(r.StartIp) + 1;
            Assert.Equal(span, r.AddressCount);
            Assert.Equal(span <= 2 ? span : span - 2, r.UsableCount);
        }
    }

    [Theory]
    [InlineData(31)]
    [InlineData(32)]
    public void ASlashThirtyOneOrThirtyTwo_HasNoReservedAddresses(int cidr)
    {
        IPRange r = Assert.Single(_svc.CalculateUnallocatedRanges("10.0.0.0", cidr, [], []));
        Assert.Equal(r.AddressCount, r.UsableCount);
    }

    [Fact]
    public void MaxUsable_IsTheBlockMinusItsOwnNetworkAndBroadcast()
    {
        IPRange whole = Assert.Single(_svc.CalculateUnallocatedRanges("10.0.0.0", 24, [], []));
        Assert.Equal(256, whole.AddressCount);
        Assert.Equal(254, whole.UsableCount);

        List<IPRange> split = [.. _svc.CalculateUnallocatedRanges(
            "10.0.0.0", 24, [new Subnet { NetworkAddress = "10.0.0.64", Cidr = 26 }], [])];

        Assert.Equal(64, split[0].AddressCount);
        Assert.Equal(62, split[0].UsableCount);
        Assert.Equal(128, split[1].AddressCount);
        Assert.Equal(126, split[1].UsableCount);
    }

    private static long ToUint(string ip)
    {
        string[] o = ip.Split('.');
        return (long.Parse(o[0]) << 24) | (long.Parse(o[1]) << 16) | (long.Parse(o[2]) << 8) | long.Parse(o[3]);
    }

    private static IReadOnlyList<ChildSubnetSuggestion> SuggestionsFor(IpUtilityService svc, string net, int cidr, params string[] kids)
    {
        List<Subnet> children = [.. kids.Select(k => new Subnet
        {
            NetworkAddress = k.Split('/')[0],
            Cidr = int.Parse(k.Split('/')[1])
        })];

        return svc.SuggestChildSubnets(cidr, [.. svc.CalculateUnallocatedRanges(net, cidr, children, [])]);
    }

    [Fact]
    public void SuggestChildSubnets_SlashZeroParent_OffersTheOtherHalfForASlashOne()
    {
        IReadOnlyList<ChildSubnetSuggestion> suggestions = SuggestionsFor(_svc, "0.0.0.0", 0, "64.0.0.0/2");

        Assert.Equal(2, suggestions.Count);
        Assert.Equal("0.0.0.0", suggestions[0].StartIp);
        Assert.Equal(2, suggestions[0].RecommendedCidr);
        Assert.Equal("128.0.0.0", suggestions[0].NetworkAddressByCidr[1]);
        Assert.Equal("0.0.0.0", suggestions[0].NetworkAddressByCidr[2]);
        Assert.Equal("128.0.0.0", suggestions[1].StartIp);
        Assert.Equal(1, suggestions[1].RecommendedCidr);
        Assert.Equal("128.0.0.0", suggestions[1].NetworkAddressByCidr[1]);
    }

    [Fact]
    public void SuggestChildSubnets_AtTheTopOfTheAddressSpace_DoesNotWrapToZero()
    {
        IReadOnlyList<ChildSubnetSuggestion> suggestions =
            SuggestionsFor(_svc, "255.255.255.0", 24, "255.255.255.0/26", "255.255.255.128/26");

        Assert.Equal(2, suggestions.Count);
        Assert.Equal("255.255.255.64", suggestions[0].StartIp);
        Assert.Equal("255.255.255.192", suggestions[1].StartIp);

        foreach (ChildSubnetSuggestion suggestion in suggestions)
        {
            Assert.Null(suggestion.NetworkAddressByCidr[25]);
            Assert.Equal(26, suggestion.RecommendedCidr);
            Assert.DoesNotContain("0.0.0.0", suggestion.NetworkAddressByCidr.Values);
        }

        Assert.Equal("255.255.255.192", suggestions[1].NetworkAddressByCidr[26]);
        Assert.Equal("255.255.255.192", suggestions[1].NetworkAddressByCidr[32]);
    }

    [Fact]
    public void SuggestChildSubnets_SlashThirtyOneParent_OffersOnlySlashThirtyTwo()
    {
        IReadOnlyList<ChildSubnetSuggestion> suggestions = SuggestionsFor(_svc, "10.0.0.0", 31, "10.0.0.0/32");

        ChildSubnetSuggestion suggestion = Assert.Single(suggestions);
        Assert.Equal("10.0.0.1", suggestion.StartIp);
        Assert.Equal(32, suggestion.RecommendedCidr);
        Assert.Equal([32], suggestion.NetworkAddressByCidr.Keys);
        Assert.Equal("10.0.0.1", suggestion.NetworkAddressByCidr[32]);
    }

    [Fact]
    public void SuggestChildSubnets_UnalignedRangeStart_SlidesToTheNextAlignedBlock()
    {
        IReadOnlyList<ChildSubnetSuggestion> suggestions =
            SuggestionsFor(_svc, "10.0.0.0", 24, "10.0.0.0/25", "10.0.0.129/32");

        Assert.Equal(2, suggestions.Count);

        ChildSubnetSuggestion single = suggestions[0];
        Assert.Equal("10.0.0.128", single.StartIp);
        Assert.Equal(32, single.RecommendedCidr);
        Assert.Equal("10.0.0.192", single.NetworkAddressByCidr[26]);
        Assert.Equal("10.0.0.130", single.NetworkAddressByCidr[31]);
        Assert.Null(single.NetworkAddressByCidr[25]);

        ChildSubnetSuggestion tail = suggestions[1];
        Assert.Equal("10.0.0.130", tail.StartIp);
        Assert.Equal(31, tail.RecommendedCidr);
        Assert.Equal("10.0.0.192", tail.NetworkAddressByCidr[26]);
        Assert.Equal("10.0.0.130", tail.NetworkAddressByCidr[31]);
        Assert.Equal("10.0.0.130", tail.NetworkAddressByCidr[32]);
        Assert.Null(tail.NetworkAddressByCidr[25]);
    }

    [Fact]
    public void SuggestChildSubnets_NoRanges_ReturnsNothing() =>
        Assert.Empty(_svc.SuggestChildSubnets(24, []));

    [Theory]
    [InlineData(-1)]
    [InlineData(32)]
    [InlineData(33)]
    public void SuggestChildSubnets_ParentCidrOutsideZeroToThirtyOne_Throws(int parentCidr) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _svc.SuggestChildSubnets(parentCidr, []));
}
