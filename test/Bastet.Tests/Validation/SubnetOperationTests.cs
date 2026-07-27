using Bastet.Models;
using Bastet.Models.DTOs;
using Bastet.Services;
using Bastet.Services.Validation;

namespace Bastet.Tests.Validation;

public class SubnetOperationTests
{
    private readonly IIpUtilityService _ipUtilityService;
    private readonly SubnetValidationService _validationService;

    public SubnetOperationTests()
    {
        _ipUtilityService = new IpUtilityService();
        _validationService = new SubnetValidationService(_ipUtilityService);
    }

    [Fact]
    public void ValidateSiblingOverlap_IdenticalSubnet_ReturnsInvalid()
    {
        // Arrange
        string networkAddress = "10.0.0.0";
        int cidr = 24;

        List<Subnet> siblings =
        [
            new() { Id = 1, Name = "Existing Sibling", NetworkAddress = "10.0.0.0", Cidr = 24 }
        ];

        // Act
        ValidationResult result = _validationService.ValidateSiblingOverlap(networkAddress, cidr, siblings);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SUBNET_OVERLAP");
    }

}
