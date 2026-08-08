using Bastet.Models;
using Bastet.Services;
using Bastet.Services.Validation;

namespace Bastet.Tests.Validation;

public class OverlapTests
{
    private readonly IIpUtilityService _ipUtilityService;
    private readonly SubnetValidationService _validationService;

    public OverlapTests()
    {
        _ipUtilityService = new IpUtilityService();
        _validationService = new SubnetValidationService(_ipUtilityService);
    }

    [Fact]
    public void ValidateSiblingOverlap_NoSiblings_ReturnsValid()
    {

        List<Subnet> siblings = [];

        ValidationResult result = _validationService.ValidateSiblingOverlap("192.168.0.0", 24, siblings);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSiblingOverlap_NonOverlappingSiblings_ReturnsValid()
    {

        List<Subnet> siblings =
        [
            new() { Id = 1, Name = "Subnet 1", NetworkAddress = "10.0.0.0", Cidr = 24 },
            new() { Id = 2, Name = "Subnet 2", NetworkAddress = "10.0.1.0", Cidr = 24 },
            new() { Id = 3, Name = "Subnet 3", NetworkAddress = "10.0.2.0", Cidr = 24 }
        ];

        ValidationResult result = _validationService.ValidateSiblingOverlap("10.0.3.0", 24, siblings);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSiblingOverlap_CompleteOverlap_ReturnsInvalid()
    {

        List<Subnet> siblings =
        [
            new() { Id = 1, Name = "Subnet 1", NetworkAddress = "10.0.0.0", Cidr = 16 }
        ];

        ValidationResult result = _validationService.ValidateSiblingOverlap("10.0.0.0", 24, siblings);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SUBNET_OVERLAP");
    }

    [Fact]
    public void ValidateSiblingOverlap_PartialOverlap_ReturnsInvalid()
    {

        List<Subnet> siblings =
        [
            new() { Id = 1, Name = "Subnet 1", NetworkAddress = "10.0.0.0", Cidr = 24 }
        ];

        ValidationResult result = _validationService.ValidateSiblingOverlap("10.0.0.128", 25, siblings);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SUBNET_OVERLAP");
    }

    [Fact]
    public void ValidateSiblingOverlap_NewSubnetContainsSibling_ReturnsInvalid()
    {

        List<Subnet> siblings =
        [
            new() { Id = 1, Name = "Subnet 1", NetworkAddress = "10.0.0.0", Cidr = 24 }
        ];

        ValidationResult result = _validationService.ValidateSiblingOverlap("10.0.0.0", 16, siblings);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SUBNET_OVERLAP");
    }

    [Fact]
    public void ValidateSiblingOverlap_AdjacentNonOverlapping_ReturnsValid()
    {

        List<Subnet> siblings =
        [
            new() { Id = 1, Name = "Subnet 1", NetworkAddress = "10.0.0.0", Cidr = 24 }
        ];

        ValidationResult result = _validationService.ValidateSiblingOverlap("10.0.1.0", 24, siblings);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSiblingOverlap_MultipleSiblings_OverlapsWithOne_ReturnsInvalid()
    {

        List<Subnet> siblings =
        [
            new() { Id = 1, Name = "Subnet 1", NetworkAddress = "10.0.0.0", Cidr = 24 },
            new() { Id = 2, Name = "Subnet 2", NetworkAddress = "10.0.1.0", Cidr = 24 },
            new() { Id = 3, Name = "Subnet 3", NetworkAddress = "10.0.2.0", Cidr = 24 }
        ];

        ValidationResult result = _validationService.ValidateSiblingOverlap("10.0.1.0", 25, siblings);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SUBNET_OVERLAP");
    }
}
