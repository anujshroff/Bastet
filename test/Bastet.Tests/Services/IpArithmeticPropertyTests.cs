using Bastet.Models;
using Bastet.Services;

namespace Bastet.Tests.Services;

public class IpArithmeticPropertyTests
{
    private readonly IpUtilityService _svc = new();

    private static uint ToUint(string ip)
    {
        string[] o = ip.Split('.');
        return (uint.Parse(o[0]) << 24) | (uint.Parse(o[1]) << 16) | (uint.Parse(o[2]) << 8) | uint.Parse(o[3]);
    }

    private static string ToIp(uint v) => $"{v >> 24}.{(v >> 16) & 255}.{(v >> 8) & 255}.{v & 255}";

    private static IEnumerable<(string Net, int Cidr)> Networks()
    {
        foreach (int cidr in Enumerable.Range(0, 33))
        {
            uint size = cidr == 0 ? 0u : 1u << (32 - cidr);
            yield return (ToIp(0), cidr);
            if (cidr > 0)
            {
                yield return (ToIp(0xFFFFFFFF & ~(size - 1)), cidr);
                yield return (ToIp((10u << 24) & ~(size - 1)), cidr);
                yield return (ToIp(((192u << 24) | (168u << 16) | (5u << 8)) & ~(size - 1)), cidr);
            }
        }
    }

    [Fact]
    public void SubnetMask_HasExactlyCidrLeadingOneBits()
    {
        for (int cidr = 0; cidr <= 32; cidr++)
        {
            uint mask = ToUint(_svc.CalculateSubnetMask(cidr));
            uint expected = cidr == 0 ? 0u : ~((1u << (32 - cidr)) - 1);
            Assert.Equal(expected, mask);
            Assert.Equal(cidr, System.Numerics.BitOperations.PopCount(mask));
        }
    }

    [Fact]
    public void Broadcast_IsNetworkPlusSizeMinusOne()
    {
        foreach ((string net, int cidr) in Networks())
        {
            long size = 1L << (32 - cidr);
            uint expected = (uint)(ToUint(net) + size - 1);
            Assert.Equal(ToIp(expected), _svc.CalculateBroadcastAddress(net, cidr));
        }
    }

    [Fact]
    public void TotalAddresses_IsTwoToThePowerOfHostBits()
    {
        for (int cidr = 0; cidr <= 32; cidr++)
        {
            Assert.Equal(1L << (32 - cidr), _svc.CalculateTotalIpAddresses(cidr));
        }
    }

    [Fact]
    public void UsableAddresses_IsTotalMinusNetworkAndBroadcast_ExceptSlash31And32()
    {
        for (int cidr = 0; cidr <= 32; cidr++)
        {
            long total = _svc.CalculateTotalIpAddresses(cidr);
            long expected = total <= 2 ? total : total - 2;
            Assert.Equal(expected, _svc.CalculateUsableIpAddresses(cidr));
        }
    }

    [Fact]
    public void IsValidSubnet_IsTrueExactlyWhenThereAreNoHostBitsSet()
    {
        foreach ((string net, int cidr) in Networks())
        {
            Assert.True(_svc.IsValidSubnet(net, cidr), $"{net}/{cidr} should be aligned");

            if (cidr < 32)
            {
                string offBoundary = ToIp(ToUint(net) + 1);
                Assert.False(_svc.IsValidSubnet(offBoundary, cidr), $"{offBoundary}/{cidr} is not aligned");
            }
        }
    }

    [Fact]
    public void Containment_AgreesWithAddressRanges()
    {
        foreach ((string parent, int pc) in Networks())
        {
            if (pc >= 32) { continue; }

            uint pStart = ToUint(parent);
            uint pEnd = ToUint(_svc.CalculateBroadcastAddress(parent, pc));

            foreach (int cc in new[] { pc + 1, Math.Min(32, pc + 4), 32 })
            {
                uint cSize = 1u << (32 - cc);
                foreach (uint child in new[] { pStart, pEnd - cSize + 1 })
                {
                    bool contained = _svc.IsSubnetContainedInParent(ToIp(child), cc, parent, pc);
                    bool withinRange = child >= pStart && child + cSize - 1 <= pEnd;
                    Assert.Equal(withinRange, contained);
                }
            }

            Assert.False(_svc.IsSubnetContainedInParent(parent, pc, parent, pc),
                "a subnet is not strictly contained in itself");
        }
    }

    [Fact]
    public void IsIpInSubnet_AgreesWithNetworkToBroadcast()
    {
        foreach ((string net, int cidr) in Networks())
        {
            uint start = ToUint(net);
            uint end = ToUint(_svc.CalculateBroadcastAddress(net, cidr));

            foreach (uint probe in new[] { start, end, start == 0 ? 0 : start - 1, end == uint.MaxValue ? end : end + 1 })
            {
                bool expected = probe >= start && probe <= end;
                Assert.Equal(expected, _svc.IsIpInSubnet(ToIp(probe), net, cidr));
            }
        }
    }

    public static TheoryData<string, int, string[]> Layouts => new()
    {
        { "10.0.0.0", 24, [] },
        { "10.0.0.0", 24, ["10.0.0.0/25"] },
        { "10.0.0.0", 24, ["10.0.0.128/25"] },
        { "10.0.0.0", 24, ["10.0.0.64/26"] },
        { "10.0.0.0", 24, ["10.0.0.0/26", "10.0.0.128/26"] },
        { "10.0.0.0", 30, ["10.0.0.0/31"] },
        { "10.0.0.0", 31, [] },
        { "10.0.0.0", 32, [] },
        { "10.20.0.0", 16, ["10.20.4.0/22"] },
        { "0.0.0.0", 0, ["64.0.0.0/2"] },
        { "255.255.255.0", 24, ["255.255.255.128/25"] },
        { "10.0.0.0", 24, ["10.0.0.32/27", "10.0.0.128/26"] },
        { "10.0.0.0", 24, ["10.0.0.0/25", "10.0.0.129/32"] },
        { "255.255.255.0", 24, ["255.255.255.0/26", "255.255.255.128/26"] },
    };

    [Theory]
    [MemberData(nameof(Layouts))]
    public void UnallocatedRanges_AreDisjoint_Ordered_InsideTheParent_AndNeverOverlapAllocations(
        string net, int cidr, string[] kids)
    {
        List<Subnet> children = [.. kids.Select(k => new Subnet
        {
            NetworkAddress = k.Split('/')[0],
            Cidr = int.Parse(k.Split('/')[1])
        })];

        uint pStart = ToUint(net);
        uint pEnd = ToUint(_svc.CalculateBroadcastAddress(net, cidr));

        List<IPRange> ranges = [.. _svc.CalculateUnallocatedRanges(net, cidr, children, [])];

        long freeTotal = 0;
        uint? previousEnd = null;

        foreach (IPRange r in ranges)
        {
            uint s = ToUint(r.StartIp);
            uint e = ToUint(r.EndIp);

            Assert.True(s <= e, $"{r.StartIp}-{r.EndIp} is inverted");
            Assert.True(s >= pStart && e <= pEnd, $"{r.StartIp}-{r.EndIp} escapes {net}/{cidr}");
            Assert.Equal(e - s + 1, r.AddressCount);
            Assert.Equal(r.AddressCount <= 2 ? r.AddressCount : r.AddressCount - 2, r.UsableCount);

            if (previousEnd is uint prev)
            {
                Assert.True(s > prev + 1 || (prev == uint.MaxValue), $"{r.StartIp} is not after the previous range");
            }
            previousEnd = e;

            foreach (Subnet c in children)
            {
                uint cs = ToUint(c.NetworkAddress);
                uint ce = cs + (1u << (32 - c.Cidr)) - 1;
                Assert.False(s <= ce && cs <= e, $"free {r.StartIp}-{r.EndIp} overlaps allocated {c.NetworkAddress}/{c.Cidr}");
            }

            freeTotal += r.AddressCount;
        }

        long allocated = children.Sum(c => 1L << (32 - c.Cidr));
        Assert.Equal(_svc.CalculateTotalIpAddresses(cidr), freeTotal + allocated);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void ChildSubnetSuggestions_AreAligned_Free_Lowest_AndRecommendedIsTheSmallestCidrThatFitsAtTheStart(
        string net, int cidr, string[] kids)
    {
        List<Subnet> children = [.. kids.Select(k => new Subnet
        {
            NetworkAddress = k.Split('/')[0],
            Cidr = int.Parse(k.Split('/')[1])
        })];

        List<IPRange> ranges = [.. _svc.CalculateUnallocatedRanges(net, cidr, children, [])];

        if (cidr == 32)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _svc.SuggestChildSubnets(cidr, ranges));
            return;
        }

        IReadOnlyList<ChildSubnetSuggestion> suggestions = _svc.SuggestChildSubnets(cidr, ranges);

        Assert.Equal(ranges.Select(r => r.StartIp), suggestions.Select(s => s.StartIp));

        long parentEnd = ToUint(_svc.CalculateBroadcastAddress(net, cidr));
        List<(long Start, long End)> free = [.. ranges.Select(r => ((long)ToUint(r.StartIp), (long)ToUint(r.EndIp)))];
        int[] childCidrs = [.. Enumerable.Range(cidr + 1, 32 - cidr)];

        for (int i = 0; i < ranges.Count; i++)
        {
            ChildSubnetSuggestion suggestion = suggestions[i];
            long start = ToUint(ranges[i].StartIp);
            long end = ToUint(ranges[i].EndIp);

            Assert.Equal(childCidrs, suggestion.NetworkAddressByCidr.Keys.Order());

            int expectedRecommended = childCidrs.First(c =>
            {
                long size = 1L << (32 - c);
                return start % size == 0 && start + size - 1 <= end;
            });
            Assert.Equal(expectedRecommended, suggestion.RecommendedCidr);

            foreach (int c in childCidrs)
            {
                long size = 1L << (32 - c);
                long? oracle = null;
                for (long candidate = (start + size - 1) / size * size; candidate + size - 1 <= parentEnd; candidate += size)
                {
                    long candidateEnd = candidate + size - 1;
                    if (free.Any(f => f.Start <= candidate && candidateEnd <= f.End))
                    {
                        oracle = candidate;
                        break;
                    }
                }

                string? actual = suggestion.NetworkAddressByCidr[c];

                if (oracle is null)
                {
                    Assert.Null(actual);
                    continue;
                }

                Assert.NotNull(actual);
                Assert.Equal(ToIp((uint)oracle.Value), actual);
                Assert.True(_svc.IsValidSubnet(actual, c), $"{actual}/{c} is not aligned");
                Assert.True(_svc.IsSubnetContainedInParent(actual, c, net, cidr), $"{actual}/{c} escapes {net}/{cidr}");
                Assert.True(oracle.Value >= start, $"{actual}/{c} lies before the range start {ranges[i].StartIp}");

                foreach (Subnet child in children)
                {
                    long cs = ToUint(child.NetworkAddress);
                    long ce = cs + (1L << (32 - child.Cidr)) - 1;
                    Assert.False(oracle.Value <= ce && cs <= oracle.Value + size - 1,
                        $"suggested {actual}/{c} overlaps allocated {child.NetworkAddress}/{child.Cidr}");
                }

                if (c >= suggestion.RecommendedCidr)
                {
                    Assert.Equal(ranges[i].StartIp, actual);
                }
                else
                {
                    Assert.NotEqual(ranges[i].StartIp, actual);
                }
            }
        }
    }

    [Fact]
    public void TheFreeSpaceUsableRule_MatchesTheCidrUsableRule_ForBlocksThatAreWholeSubnets()
    {
        foreach (int cidr in new[] { 24, 25, 26, 30, 31, 32 })
        {
            IPRange r = Assert.Single(_svc.CalculateUnallocatedRanges("10.0.0.0", cidr, [], []));
            Assert.Equal(_svc.CalculateTotalIpAddresses(cidr), r.AddressCount);
            Assert.Equal(_svc.CalculateUsableIpAddresses(cidr), r.UsableCount);
        }
    }
}
