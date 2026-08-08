using Bastet.Services;
using Bastet.Services.Validation;

namespace Bastet.Tests.Validation;

public class SubnetFormatTests
{
    private readonly IIpUtilityService _ipUtilityService;
    private readonly SubnetValidationService _validationService;

    public SubnetFormatTests()
    {
        _ipUtilityService = new IpUtilityService();
        _validationService = new SubnetValidationService(_ipUtilityService);
    }

    [Fact]
    public void ValidateSubnetFormat_ValidInput_ReturnsValid()
    {

        ValidationResult result = _validationService.ValidateSubnetFormat("192.168.1.0", 24);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetFormat_InvalidIPAddress_ReturnsInvalid()
    {

        ValidationResult result = _validationService.ValidateSubnetFormat("not-an-ip", 24);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_NETWORK_FORMAT");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(33)]
    public void ValidateSubnetFormat_InvalidCIDR_ReturnsInvalid(int cidr)
    {

        ValidationResult result = _validationService.ValidateSubnetFormat("192.168.1.0", cidr);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_CIDR_VALUE");
    }

    [Theory]
    [InlineData(0, "0.0.0.0")]
    [InlineData(32, "192.168.1.1")]
    public void ValidateSubnetFormat_EdgeCaseCIDR_ReturnsValid(int cidr, string ip)
    {

        ValidationResult result = _validationService.ValidateSubnetFormat(ip, cidr);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("192.168.1.1", 24)]
    [InlineData("10.1.0.1", 16)]
    public void ValidateSubnetFormat_MisalignedNetwork_ReturnsInvalid(string ip, int cidr)
    {

        ValidationResult result = _validationService.ValidateSubnetFormat(ip, cidr);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "NETWORK_CIDR_MISMATCH");
    }

    [Theory]
    [InlineData("10.0.0.0", 8)]
    [InlineData("172.16.0.0", 16)]
    [InlineData("192.168.0.0", 24)]
    [InlineData("10.10.0.0", 15)]
    public void ValidateSubnetFormat_CorrectlyAligned_ReturnsValid(string ip, int cidr)
    {

        ValidationResult result = _validationService.ValidateSubnetFormat(ip, cidr);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
}
