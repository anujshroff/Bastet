using Bastet.Models;
using Bastet.Services;

namespace Bastet.Tests.Validation;

/// <summary>
/// Tests for subnet property calculations in the IpUtilityService
/// </summary>
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
        // Act
        string result = _ipUtilityService.CalculateSubnetMask(cidr);

        // Assert
        Assert.Equal(expectedMask, result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(33)]
    public void CalculateSubnetMask_InvalidCidr_ThrowsException(int invalidCidr) =>
        // Act & Assert
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
    [InlineData("0.0.0.0", 0, "255.255.255.255")] // The entire internet
    public void CalculateBroadcastAddress_ReturnsCorrectAddress(string networkAddress, int cidr, string expectedBroadcast)
    {
        // Act
        string result = _ipUtilityService.CalculateBroadcastAddress(networkAddress, cidr);

        // Assert
        Assert.Equal(expectedBroadcast, result);
    }

    [Fact]
    public void CalculateBroadcastAddress_NullNetworkAddress_ThrowsException()
    {
        // Arrange
        string? nullAddress = null;

        // Act & Assert - Using null-forgiving operator to suppress CS8604 warning
        Assert.Throws<ArgumentNullException>(() =>
            _ipUtilityService.CalculateBroadcastAddress(nullAddress!, 24));
    }

    [Theory]
    [InlineData("", 24)]
    [InlineData("invalid-ip", 24)]
    [InlineData("999.999.999.999", 24)]
    public void CalculateBroadcastAddress_InvalidNetworkAddress_ThrowsException(string invalidAddress, int cidr) =>
        // Act & Assert
        Assert.ThrowsAny<Exception>(() =>
            _ipUtilityService.CalculateBroadcastAddress(invalidAddress, cidr));

    [Theory]
    [InlineData("10.0.0.0", -1)]
    [InlineData("10.0.0.0", 33)]
    public void CalculateBroadcastAddress_InvalidCidr_ThrowsException(string networkAddress, int invalidCidr) =>
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ipUtilityService.CalculateBroadcastAddress(networkAddress, invalidCidr));

    #endregion

    #region IP Address Count Tests

    [Theory]
    [InlineData(0, 4294967296)] // 2^32
    [InlineData(8, 16777216)]   // 2^24
    [InlineData(16, 65536)]     // 2^16
    [InlineData(24, 256)]       // 2^8
    [InlineData(30, 4)]         // 2^2
    [InlineData(31, 2)]         // 2^1
    [InlineData(32, 1)]         // 2^0
    public void CalculateTotalIpAddresses_ReturnsCorrectCount(int cidr, long expectedTotal)
    {
        // Act
        long totalAddresses = _ipUtilityService.CalculateTotalIpAddresses(cidr);

        // Assert
        Assert.Equal(expectedTotal, totalAddresses);
    }

    [Theory]
    [InlineData(0, 4294967294)] // 2^32 - 2
    [InlineData(8, 16777214)]   // 2^24 - 2
    [InlineData(16, 65534)]     // 2^16 - 2
    [InlineData(24, 254)]       // 2^8 - 2
    [InlineData(30, 2)]         // 2^2 - 2
    [InlineData(31, 2)]         // Special case: 2 usable addresses
    [InlineData(32, 1)]         // Special case: 1 usable address
    public void CalculateUsableIpAddresses_ReturnsCorrectCount(int cidr, long expectedUsable)
    {
        // Act
        long usableAddresses = _ipUtilityService.CalculateUsableIpAddresses(cidr);

        // Assert
        Assert.Equal(expectedUsable, usableAddresses);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(33)]
    public void CalculateIpAddresses_InvalidCidr_ThrowsException(int invalidCidr)
    {
        // Act & Assert
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
        // Act
        bool result = _ipUtilityService.IsValidSubnet(networkAddress, cidr);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("10.0.0.1", 24)]    // Not aligned for /24
    [InlineData("192.168.1.5", 24)] // Not aligned for /24
    [InlineData("172.16.1.1", 16)]  // Not aligned for /16
    [InlineData("10.1.0.0", 8)]     // Not aligned for /8
    public void IsValidSubnet_MisalignedNetworkAddress_ReturnsFalse(string networkAddress, int cidr)
    {
        // Act
        bool result = _ipUtilityService.IsValidSubnet(networkAddress, cidr);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("", 24)]
    [InlineData("invalid-ip", 24)]
    [InlineData("10.0.0.0", -1)]
    [InlineData("10.0.0.0", 33)]
    public void IsValidSubnet_InvalidInput_ReturnsFalse(string networkAddress, int cidr)
    {
        // Act
        bool result = _ipUtilityService.IsValidSubnet(networkAddress, cidr);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Unallocated Ranges Tests

    [Fact]
    public void CalculateUnallocatedRanges_EmptySubnet_ReturnsEntireRange()
    {
        // Arrange
        string networkAddress = "10.0.0.0";
        int cidr = 24;
        List<Subnet> childSubnets = [];

        // Act
        List<IPRange> result = [.. _ipUtilityService.CalculateUnallocatedRanges(networkAddress, cidr, childSubnets)];

        // Assert
        Assert.Single(result);
        Assert.Equal("10.0.0.0", result[0].StartIp);
        Assert.Equal("10.0.0.255", result[0].EndIp);
        Assert.Equal(254, result[0].AddressCount); // Excludes network and broadcast addresses
    }

    [Fact]
    public void CalculateUnallocatedRanges_WithSingleChild_ReturnsCorrectGaps()
    {
        // Arrange
        string networkAddress = "10.0.0.0";
        int cidr = 24;

        List<Subnet> childSubnets = [
            new() { NetworkAddress = "10.0.0.0", Cidr = 25 }  // 10.0.0.0 - 10.0.0.127
        ];

        // Act
        List<IPRange> result = [.. _ipUtilityService.CalculateUnallocatedRanges(networkAddress, cidr, childSubnets)];

        // Assert
        Assert.Single(result);

        // Gap from 10.0.0.128 to 10.0.0.254 (Excluding broadcast address)
        Assert.Equal("10.0.0.128", result[0].StartIp);
        Assert.Equal("10.0.0.254", result[0].EndIp);
        // 10.0.0.128-10.0.0.254 inclusive is 127 addresses. The broadcast (.255) is already
        // excluded by EndIp; it must not be subtracted a second time from the count.
        Assert.Equal(127, result[0].AddressCount);
    }

    // -------------------------------------------------------------------------
    // A gap that begins at the network address always has one unusable address in it - the
    // network address itself - however small the gap is.
    // -------------------------------------------------------------------------

    /// <summary>
    /// The cheapest repro: a lone host IP at 10.0.0.1 leaves a leading gap of exactly one address,
    /// the network address, of which none is assignable.
    /// </summary>
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

    /// <summary>A leading gap of two holds the network address plus one assignable address.</summary>
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

    /// <summary>
    /// A trailing gap of exactly one address. The removed special case got this right via a
    /// hard-coded fallback, so it is pinned here to prove the general formula also does.
    /// 10.0.0.0/24 with .0-.253 consumed leaves only .254, the last usable address.
    /// </summary>
    [Fact]
    public void CalculateUnallocatedRanges_TrailingGapOfOne_ReportsOneAddress()
    {
        List<Subnet> childSubnets = [
            new() { NetworkAddress = "10.0.0.0", Cidr = 25 },    // .0   - .127
            new() { NetworkAddress = "10.0.0.128", Cidr = 26 },  // .128 - .191
            new() { NetworkAddress = "10.0.0.192", Cidr = 27 },  // .192 - .223
            new() { NetworkAddress = "10.0.0.224", Cidr = 28 },  // .224 - .239
            new() { NetworkAddress = "10.0.0.240", Cidr = 29 },  // .240 - .247
            new() { NetworkAddress = "10.0.0.248", Cidr = 30 },  // .248 - .251
            new() { NetworkAddress = "10.0.0.252", Cidr = 31 },  // .252 - .253
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
        // Arrange
        string networkAddress = "10.0.0.0";
        int cidr = 24;

        List<Subnet> childSubnets = [
            new() { NetworkAddress = "10.0.0.0", Cidr = 26 },   // 10.0.0.0 - 10.0.0.63
            new() { NetworkAddress = "10.0.0.128", Cidr = 26 }  // 10.0.0.128 - 10.0.0.191
        ];

        // Act
        List<IPRange> result = [.. _ipUtilityService.CalculateUnallocatedRanges(networkAddress, cidr, childSubnets)];

        // Assert
        Assert.Equal(2, result.Count);

        // First gap: 10.0.0.64 - 10.0.0.127
        Assert.Equal("10.0.0.64", result[0].StartIp);
        Assert.Equal("10.0.0.127", result[0].EndIp);
        Assert.Equal(64, result[0].AddressCount);

        // Second gap: 10.0.0.192 - 10.0.0.254 (Excluding broadcast address)
        Assert.Equal("10.0.0.192", result[1].StartIp);
        Assert.Equal("10.0.0.254", result[1].EndIp);
        // 10.0.0.192-10.0.0.254 inclusive is 63 - counted the same way as the 64 above.
        Assert.Equal(63, result[1].AddressCount);
    }

    [Fact]
    public void CalculateUnallocatedRanges_SpecialCaseCidr31_HandlesCorrectly()
    {
        // Arrange
        string networkAddress = "10.0.0.0";
        int cidr = 31;
        List<Subnet> childSubnets = [];

        // Act
        List<IPRange> result = [.. _ipUtilityService.CalculateUnallocatedRanges(networkAddress, cidr, childSubnets)];

        // Assert
        Assert.Single(result);
        Assert.Equal("10.0.0.0", result[0].StartIp);
        Assert.Equal("10.0.0.1", result[0].EndIp);
        Assert.Equal(2, result[0].AddressCount); // Both addresses are usable in /31
    }

    [Fact]
    public void CalculateUnallocatedRanges_Cidr32_SingleHostAddress()
    {
        // Arrange
        string networkAddress = "10.0.0.1";
        int cidr = 32;
        List<Subnet> childSubnets = [];

        // Act
        List<IPRange> result = [.. _ipUtilityService.CalculateUnallocatedRanges(networkAddress, cidr, childSubnets)];

        // Assert
        Assert.Single(result);
        Assert.Equal("10.0.0.1", result[0].StartIp);
        Assert.Equal("10.0.0.1", result[0].EndIp);
        Assert.Equal(1, result[0].AddressCount); // Single host address
    }

    [Fact]
    public void CalculateUnallocatedRanges_CompletelyAllocated_ReturnsEntireRange()
    {
        // Arrange
        string networkAddress = "10.0.0.0";
        int cidr = 24;

        // Child subnet that exactly matches the parent
        // Note: The current implementation does not filter out child subnets that exactly match the parent
        List<Subnet> childSubnets = [
            new() { NetworkAddress = "10.0.0.0", Cidr = 24 }
        ];

        // Act
        List<IPRange> result = [.. _ipUtilityService.CalculateUnallocatedRanges(networkAddress, cidr, childSubnets)];

        // Assert - The implementation returns the entire range when a child exactly matches the parent
        Assert.Single(result);
        Assert.Equal("10.0.0.0", result[0].StartIp);
        Assert.Equal("10.0.0.255", result[0].EndIp);
        Assert.Equal(254, result[0].AddressCount);
    }

    [Fact]
    public void CalculateUnallocatedRanges_NullNetworkAddress_ThrowsException()
    {
        // Arrange
        string? nullNetwork = null;
        List<Subnet> childSubnets = [];

        // Act & Assert - Need to use 'as' to avoid warning CS8604
        Assert.Throws<ArgumentNullException>(() =>
            _ipUtilityService.CalculateUnallocatedRanges(nullNetwork!, 24, childSubnets));
    }

    [Fact]
    public void CalculateUnallocatedRanges_InvalidCidr_ThrowsException()
    {
        // Arrange
        List<Subnet> childSubnets = [];

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ipUtilityService.CalculateUnallocatedRanges("10.0.0.0", -1, childSubnets));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ipUtilityService.CalculateUnallocatedRanges("10.0.0.0", 33, childSubnets));
    }

    [Fact]
    public void CalculateUnallocatedRanges_InvalidIpFormat_ThrowsException()
    {
        // Arrange
        List<Subnet> childSubnets = [];

        // Act & Assert
        Assert.ThrowsAny<Exception>(() =>
            _ipUtilityService.CalculateUnallocatedRanges("invalid-ip", 24, childSubnets));
    }

    #endregion

    #region Parent-Child Subnet Containment Tests

    [Theory]
    [InlineData("10.0.0.0", 24, "10.0.0.0", 16, true)]     // Child is within parent
    [InlineData("10.0.1.0", 24, "10.0.0.0", 16, true)]     // Child is within parent
    [InlineData("10.0.0.0", 25, "10.0.0.0", 24, true)]     // Child is within parent
    [InlineData("10.0.0.128", 25, "10.0.0.0", 24, true)]   // Child is within parent
    [InlineData("10.0.0.0", 24, "172.16.0.0", 16, false)]  // Different networks
    [InlineData("10.0.0.0", 16, "10.0.0.0", 24, false)]    // Child CIDR < parent CIDR (larger network)
    [InlineData("10.0.0.0", 24, "10.0.0.0", 24, false)]    // Same subnet
    [InlineData("10.1.0.0", 24, "10.0.0.0", 16, false)]    // 10.1.0.0 is not in 10.0.0.0/16 as per implementation
    [InlineData("10.0.1.0", 24, "10.0.0.0", 24, false)]    // Different subnets
    public void IsSubnetContainedInParent_CorrectlyIdentifiesContainment(
        string childNetwork, int childCidr, string parentNetwork, int parentCidr, bool expectedResult)
    {
        // Act
        bool result = _ipUtilityService.IsSubnetContainedInParent(
            childNetwork, childCidr, parentNetwork, parentCidr);

        // Assert
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
        // Act
        bool result = _ipUtilityService.IsSubnetContainedInParent(
            childNetwork, childCidr, parentNetwork, parentCidr);

        // Assert
        Assert.False(result);
    }

    #endregion
}
