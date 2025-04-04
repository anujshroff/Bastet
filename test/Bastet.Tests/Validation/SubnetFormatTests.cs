using Bastet.Services;
using Bastet.Services.Validation;

namespace Bastet.Tests.Validation;

public class SubnetFormatTests
{
    private readonly IIpUtilityService _ipUtilityService;
    private readonly ISubnetValidationService _validationService;

    public SubnetFormatTests()
    {
        _ipUtilityService = new IpUtilityService();
        _validationService = new SubnetValidationService(_ipUtilityService);
    }

    [Fact]
    public void ValidateSubnetFormat_ValidInput_ReturnsValid()
    {
        // Arrange & Act
        var result = _validationService.ValidateSubnetFormat("192.168.1.0", 24);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetFormat_InvalidIPAddress_ReturnsInvalid()
    {
        // Arrange & Act
        var result = _validationService.ValidateSubnetFormat("not-an-ip", 24);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_NETWORK_FORMAT");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(33)]
    public void ValidateSubnetFormat_InvalidCIDR_ReturnsInvalid(int cidr)
    {
        // Arrange & Act
        var result = _validationService.ValidateSubnetFormat("192.168.1.0", cidr);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_CIDR_VALUE");
    }

    [Theory]
    [InlineData(0, "0.0.0.0")]
    [InlineData(32, "192.168.1.1")]
    public void ValidateSubnetFormat_EdgeCaseCIDR_ReturnsValid(int cidr, string ip)
    {
        // Arrange & Act
        var result = _validationService.ValidateSubnetFormat(ip, cidr);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("192.168.1.1", 24)] // Should be 192.168.1.0/24
    [InlineData("10.1.0.1", 16)]    // Should be 10.1.0.0/16
    public void ValidateSubnetFormat_MisalignedNetwork_ReturnsInvalid(string ip, int cidr)
    {
        // Arrange & Act
        var result = _validationService.ValidateSubnetFormat(ip, cidr);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "NETWORK_CIDR_MISMATCH");
    }

    [Theory]
    [InlineData("10.0.0.0", 8)]
    [InlineData("172.16.0.0", 16)]
    [InlineData("192.168.0.0", 24)]
    [InlineData("10.10.0.0", 15)] // 10.10.0.0 is correctly aligned for /15
    public void ValidateSubnetFormat_CorrectlyAligned_ReturnsValid(string ip, int cidr)
    {
        // Arrange & Act
        var result = _validationService.ValidateSubnetFormat(ip, cidr);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
}
