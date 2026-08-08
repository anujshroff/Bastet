using Bastet.Models;
using Bastet.Services;

namespace Bastet.Tests.Validation;

public class SubnetPropertyCalculationTests
{
    private readonly IpUtilityService _ipUtilityService;

    public SubnetPropertyCalculationTests() => _ipUtilityService = new IpUtilityService();

    #region Subnet Mask Tests

    [Theory]
    [InlineData(0, "0.0.0.0")]
    [InlineData(8, "255.0.0.0")]
    [InlineData(16, "255.255.0.0")]
    [InlineData(24, "255.255.255.0")]
    [InlineData(25, "255.255.255.128")]
    [InlineData(30, "255.255.255.252")]
    [InlineData(31, "255.255.255.254")]
    [InlineData(32, "255.255.255.255")]
    public void CalculateSubnetMask_ReturnsCorrectMask(int cidr, string expectedMask)
    {

        string result = _ipUtilityService.CalculateSubnetMask(cidr);

        Assert.Equal(expectedMask, result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(33)]
    public void CalculateSubnetMask_InvalidCidr_ThrowsException(int invalidCidr) =>

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ipUtilityService.CalculateSubnetMask(invalidCidr));

    #endregion

    #region Broadcast Address Tests

    [Theory]
    [InlineData("10.0.0.0", 24, "10.0.0.255")]
    [InlineData("192.168.1.0", 24, "192.168.1.255")]
    [InlineData("172.16.0.0", 16, "172.16.255.255")]
    [InlineData("10.0.0.0", 8, "10.255.255.255")]
    [InlineData("10.0.0.0", 30, "10.0.0.3")]
    [InlineData("10.0.0.0", 31, "10.0.0.1")]
    [InlineData("10.0.0.0", 32, "10.0.0.0")]
    [InlineData("0.0.0.0", 0, "255.255.255.255")]
    public void CalculateBroadcastAddress_ReturnsCorrectAddress(string networkAddress, int cidr, string expectedBroadcast)
    {

        string result = _ipUtilityService.CalculateBroadcastAddress(networkAddress, cidr);

        Assert.Equal(expectedBroadcast, result);
    }

    [Fact]
    public void CalculateBroadcastAddress_NullNetworkAddress_ThrowsException()
    {

        string? nullAddress = null;

        Assert.Throws<ArgumentNullException>(() =>
            _ipUtilityService.CalculateBroadcastAddress(nullAddress!, 24));
    }

    [Theory]
    [InlineData("", 24)]
    [InlineData("invalid-ip", 24)]
    [InlineData("999.999.999.999", 24)]
    public void CalculateBroadcastAddress_InvalidNetworkAddress_ThrowsException(string invalidAddress, int cidr) =>

        Assert.ThrowsAny<Exception>(() =>
            _ipUtilityService.CalculateBroadcastAddress(invalidAddress, cidr));

    [Theory]
    [InlineData("10.0.0.0", -1)]
    [InlineData("10.0.0.0", 33)]
    public void CalculateBroadcastAddress_InvalidCidr_ThrowsException(string networkAddress, int invalidCidr) =>

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ipUtilityService.CalculateBroadcastAddress(networkAddress, invalidCidr));

    #endregion

    #region IP Address Count Tests

    [Theory]
    [InlineData(0, 4294967296)]
    [InlineData(8, 16777216)]
    [InlineData(16, 65536)]
    [InlineData(24, 256)]
    [InlineData(30, 4)]
    [InlineData(31, 2)]
    [InlineData(32, 1)]
    public void CalculateTotalIpAddresses_ReturnsCorrectCount(int cidr, long expectedTotal)
    {

        long totalAddresses = _ipUtilityService.CalculateTotalIpAddresses(cidr);

        Assert.Equal(expectedTotal, totalAddresses);
    }

    [Theory]
    [InlineData(0, 4294967294)]
    [InlineData(8, 16777214)]
    [InlineData(16, 65534)]
    [InlineData(24, 254)]
    [InlineData(30, 2)]
    [InlineData(31, 2)]
    [InlineData(32, 1)]
    public void CalculateUsableIpAddresses_ReturnsCorrectCount(int cidr, long expectedUsable)
    {

        long usableAddresses = _ipUtilityService.CalculateUsableIpAddresses(cidr);

        Assert.Equal(expectedUsable, usableAddresses);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(33)]
    public void CalculateIpAddresses_InvalidCidr_ThrowsException(int invalidCidr)
    {

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ipUtilityService.CalculateTotalIpAddresses(invalidCidr));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ipUtilityService.CalculateUsableIpAddresses(invalidCidr));
    }

    #endregion

    #region Subnet Validation Tests

    [Theory]
    [InlineData("10.0.0.0", 24)]
    [InlineData("192.168.1.0", 24)]
    [InlineData("172.16.0.0", 16)]
    [InlineData("10.0.0.0", 8)]
    [InlineData("0.0.0.0", 0)]
    [InlineData("10.0.0.0", 32)]
    public void IsValidSubnet_AlignedNetworkAddress_ReturnsTrue(string networkAddress, int cidr)
    {

        bool result = _ipUtilityService.IsValidSubnet(networkAddress, cidr);

        Assert.True(result);
    }

    [Theory]
    [InlineData("10.0.0.1", 24)]
    [InlineData("192.168.1.5", 24)]
    [InlineData("172.16.1.1", 16)]
    [InlineData("10.1.0.0", 8)]
    public void IsValidSubnet_MisalignedNetworkAddress_ReturnsFalse(string networkAddress, int cidr)
    {

        bool result = _ipUtilityService.IsValidSubnet(networkAddress, cidr);

        Assert.False(result);
    }

    [Theory]
    [InlineData("", 24)]
    [InlineData("invalid-ip", 24)]
    [InlineData("10.0.0.0", -1)]
    [InlineData("10.0.0.0", 33)]
    public void IsValidSubnet_InvalidInput_ReturnsFalse(string networkAddress, int cidr)
    {

        bool result = _ipUtilityService.IsValidSubnet(networkAddress, cidr);

        Assert.False(result);
    }

    #endregion

    #region Unallocated Ranges Tests

    [Fact]
    public void CalculateUnallocatedRanges_EmptySubnet_ReturnsEntireRange()
    {

        string networkAddress = "10.0.0.0";
        int cidr = 24;
        List<Subnet> childSubnets = [];

        List<IPRange> result = [.. _ipUtilityService.CalculateUnallocatedRanges(networkAddress, cidr, childSubnets)];

        Assert.Single(result);
        Assert.Equal("10.0.0.0", result[0].StartIp);
        Assert.Equal("10.0.0.255", result[0].EndIp);
        Assert.Equal(254, result[0].AddressCount);
    }

    [Fact]
    public void CalculateUnallocatedRanges_WithSingleChild_ReturnsCorrectGaps()
    {

        string networkAddress = "10.0.0.0";
        int cidr = 24;

        List<Subnet> childSubnets = [
            new() { NetworkAddress = "10.0.0.0", Cidr = 25 }
        ];

        List<IPRange> result = [.. _ipUtilityService.CalculateUnallocatedRanges(networkAddress, cidr, childSubnets)];

        Assert.Single(result);

        Assert.Equal("10.0.0.128", result[0].StartIp);
        Assert.Equal("10.0.0.254", result[0].EndIp);

        Assert.Equal(127, result[0].AddressCount);
    }

    [Fact]
    public void CalculateUnallocatedRanges_LeadingGapOfOne_ReportsNoUsableAddresses()
    {
        List<HostIpAssignment> hostIps = [new() { IP = "10.0.0.1" }];

        List<IPRange> result = [.. _ipUtilityService.CalculateUnallocatedRanges("10.0.0.0", 24, [], hostIps)];

        IPRange leading = result[0];
        Assert.Equal("10.0.0.0", leading.StartIp);
        Assert.Equal("10.0.0.0", leading.EndIp);
        Assert.Equal(0, leading.AddressCount);
    }

    [Fact]
    public void CalculateUnallocatedRanges_LeadingGapOfTwo_ReportsOneUsableAddress()
    {
        List<HostIpAssignment> hostIps = [new() { IP = "10.0.0.2" }];

        List<IPRange> result = [.. _ipUtilityService.CalculateUnallocatedRanges("10.0.0.0", 24, [], hostIps)];

        IPRange leading = result[0];
        Assert.Equal("10.0.0.0", leading.StartIp);
        Assert.Equal("10.0.0.1", leading.EndIp);
        Assert.Equal(1, leading.AddressCount);
    }

    [Fact]
    public void CalculateUnallocatedRanges_TrailingGapOfOne_ReportsOneAddress()
    {
        List<Subnet> childSubnets = [
            new() { NetworkAddress = "10.0.0.0", Cidr = 25 },
            new() { NetworkAddress = "10.0.0.128", Cidr = 26 },
            new() { NetworkAddress = "10.0.0.192", Cidr = 27 },
            new() { NetworkAddress = "10.0.0.224", Cidr = 28 },
            new() { NetworkAddress = "10.0.0.240", Cidr = 29 },
            new() { NetworkAddress = "10.0.0.248", Cidr = 30 },
            new() { NetworkAddress = "10.0.0.252", Cidr = 31 },
        ];

        List<IPRange> result = [.. _ipUtilityService.CalculateUnallocatedRanges("10.0.0.0", 24, childSubnets)];

        IPRange last = Assert.Single(result);
        Assert.Equal("10.0.0.254", last.StartIp);
        Assert.Equal("10.0.0.254", last.EndIp);
        Assert.Equal(1, last.AddressCount);
    }

    [Fact]
    public void CalculateUnallocatedRanges_WithMultipleChildren_ReturnsCorrectGaps()
    {

        string networkAddress = "10.0.0.0";
        int cidr = 24;

        List<Subnet> childSubnets = [
            new() { NetworkAddress = "10.0.0.0", Cidr = 26 },
            new() { NetworkAddress = "10.0.0.128", Cidr = 26 }
        ];

        List<IPRange> result = [.. _ipUtilityService.CalculateUnallocatedRanges(networkAddress, cidr, childSubnets)];

        Assert.Equal(2, result.Count);

        Assert.Equal("10.0.0.64", result[0].StartIp);
        Assert.Equal("10.0.0.127", result[0].EndIp);
        Assert.Equal(64, result[0].AddressCount);

        Assert.Equal("10.0.0.192", result[1].StartIp);
        Assert.Equal("10.0.0.254", result[1].EndIp);

        Assert.Equal(63, result[1].AddressCount);
    }

    [Fact]
    public void CalculateUnallocatedRanges_SpecialCaseCidr31_HandlesCorrectly()
    {

        string networkAddress = "10.0.0.0";
        int cidr = 31;
        List<Subnet> childSubnets = [];

        List<IPRange> result = [.. _ipUtilityService.CalculateUnallocatedRanges(networkAddress, cidr, childSubnets)];

        Assert.Single(result);
        Assert.Equal("10.0.0.0", result[0].StartIp);
        Assert.Equal("10.0.0.1", result[0].EndIp);
        Assert.Equal(2, result[0].AddressCount);
    }

    [Fact]
    public void CalculateUnallocatedRanges_Cidr32_SingleHostAddress()
    {

        string networkAddress = "10.0.0.1";
        int cidr = 32;
        List<Subnet> childSubnets = [];

        List<IPRange> result = [.. _ipUtilityService.CalculateUnallocatedRanges(networkAddress, cidr, childSubnets)];

        Assert.Single(result);
        Assert.Equal("10.0.0.1", result[0].StartIp);
        Assert.Equal("10.0.0.1", result[0].EndIp);
        Assert.Equal(1, result[0].AddressCount);
    }

    [Fact]
    public void CalculateUnallocatedRanges_CompletelyAllocated_ReturnsEntireRange()
    {

        string networkAddress = "10.0.0.0";
        int cidr = 24;

        List<Subnet> childSubnets = [
            new() { NetworkAddress = "10.0.0.0", Cidr = 24 }
        ];

        List<IPRange> result = [.. _ipUtilityService.CalculateUnallocatedRanges(networkAddress, cidr, childSubnets)];

        Assert.Single(result);
        Assert.Equal("10.0.0.0", result[0].StartIp);
        Assert.Equal("10.0.0.255", result[0].EndIp);
        Assert.Equal(254, result[0].AddressCount);
    }

    [Fact]
    public void CalculateUnallocatedRanges_NullNetworkAddress_ThrowsException()
    {

        string? nullNetwork = null;
        List<Subnet> childSubnets = [];

        Assert.Throws<ArgumentNullException>(() =>
            _ipUtilityService.CalculateUnallocatedRanges(nullNetwork!, 24, childSubnets));
    }

    [Fact]
    public void CalculateUnallocatedRanges_InvalidCidr_ThrowsException()
    {

        List<Subnet> childSubnets = [];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ipUtilityService.CalculateUnallocatedRanges("10.0.0.0", -1, childSubnets));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ipUtilityService.CalculateUnallocatedRanges("10.0.0.0", 33, childSubnets));
    }

    [Fact]
    public void CalculateUnallocatedRanges_InvalidIpFormat_ThrowsException()
    {

        List<Subnet> childSubnets = [];

        Assert.ThrowsAny<Exception>(() =>
            _ipUtilityService.CalculateUnallocatedRanges("invalid-ip", 24, childSubnets));
    }

    #endregion

    #region Parent-Child Subnet Containment Tests

    [Theory]
    [InlineData("10.0.0.0", 24, "10.0.0.0", 16, true)]
    [InlineData("10.0.1.0", 24, "10.0.0.0", 16, true)]
    [InlineData("10.0.0.0", 25, "10.0.0.0", 24, true)]
    [InlineData("10.0.0.128", 25, "10.0.0.0", 24, true)]
    [InlineData("10.0.0.0", 24, "172.16.0.0", 16, false)]
    [InlineData("10.0.0.0", 16, "10.0.0.0", 24, false)]
    [InlineData("10.0.0.0", 24, "10.0.0.0", 24, false)]
    [InlineData("10.1.0.0", 24, "10.0.0.0", 16, false)]
    [InlineData("10.0.1.0", 24, "10.0.0.0", 24, false)]
    public void IsSubnetContainedInParent_CorrectlyIdentifiesContainment(
        string childNetwork, int childCidr, string parentNetwork, int parentCidr, bool expectedResult)
    {

        bool result = _ipUtilityService.IsSubnetContainedInParent(
            childNetwork, childCidr, parentNetwork, parentCidr);

        Assert.Equal(expectedResult, result);
    }

    [Theory]
    [InlineData("", 24, "10.0.0.0", 16)]
    [InlineData("10.0.0.0", 24, "", 16)]
    [InlineData("invalid-ip", 24, "10.0.0.0", 16)]
    [InlineData("10.0.0.0", 24, "invalid-ip", 16)]
    public void IsSubnetContainedInParent_InvalidInput_ReturnsFalse(
        string childNetwork, int childCidr, string parentNetwork, int parentCidr)
    {

        bool result = _ipUtilityService.IsSubnetContainedInParent(
            childNetwork, childCidr, parentNetwork, parentCidr);

        Assert.False(result);
    }

    #endregion
}
