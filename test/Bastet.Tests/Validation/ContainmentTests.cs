using Bastet.Services;
using Bastet.Services.Validation;

namespace Bastet.Tests.Validation;

public class ContainmentTests
{
    private readonly IIpUtilityService _ipUtilityService;
    private readonly SubnetValidationService _validationService;

    public ContainmentTests()
    {
        _ipUtilityService = new IpUtilityService();
        _validationService = new SubnetValidationService(_ipUtilityService);
    }

    [Fact]
    public void ValidateSubnetContainment_ValidChildInParent_ReturnsValid()
    {

        ValidationResult result = _validationService.ValidateSubnetContainment(
            "10.0.0.0", 16,
            "10.0.0.0", 8);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetContainment_ChildOutsideParent_ReturnsInvalid()
    {

        ValidationResult result = _validationService.ValidateSubnetContainment(
            "192.168.0.0", 24,
            "10.0.0.0", 8);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "NOT_IN_PARENT_RANGE");
    }

    [Fact]
    public void ValidateSubnetContainment_ChildCidrEqualToParent_ReturnsInvalid()
    {

        ValidationResult result = _validationService.ValidateSubnetContainment(
            "10.0.0.0", 16,
            "10.0.0.0", 16);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_CIDR_HIERARCHY");
    }

    [Fact]
    public void ValidateSubnetContainment_ChildCidrSmallerThanParent_ReturnsInvalid()
    {

        ValidationResult result = _validationService.ValidateSubnetContainment(
            "10.0.0.0", 8,
            "10.0.0.0", 16);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_CIDR_HIERARCHY");
    }

    [Fact]
    public void ValidateSubnetContainment_PartialOverlap_ReturnsInvalid()
    {

        ValidationResult result = _validationService.ValidateSubnetContainment(
            "10.0.128.0", 17,
            "10.0.0.0", 18);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "NOT_IN_PARENT_RANGE");
    }

    [Fact]
    public void ValidateSubnetContainment_ValidMultiLevelContainment_ReturnsValid()
    {

        ValidationResult parentInGrandparent = _validationService.ValidateSubnetContainment(
            "10.0.0.0", 16,
            "10.0.0.0", 8);

        ValidationResult childInParent = _validationService.ValidateSubnetContainment(
            "10.0.0.0", 24,
            "10.0.0.0", 16);

        ValidationResult childInGrandparent = _validationService.ValidateSubnetContainment(
            "10.0.0.0", 24,
            "10.0.0.0", 8);

        Assert.True(parentInGrandparent.IsValid);
        Assert.True(childInParent.IsValid);
        Assert.True(childInGrandparent.IsValid);
    }

    [Fact]
    public void ValidateSubnetContainment_EdgeCases_ReturnsExpectedResults()
    {

        ValidationResult result1 = _validationService.ValidateSubnetContainment(
            "10.0.0.1", 32,
            "0.0.0.0", 0);

        ValidationResult result2 = _validationService.ValidateSubnetContainment(
            "192.168.0.0", 25,
            "192.168.0.0", 24);

        ValidationResult result3 = _validationService.ValidateSubnetContainment(
            "192.168.1.0", 25,
            "192.168.0.0", 24);

        Assert.True(result1.IsValid);
        Assert.True(result2.IsValid);
        Assert.False(result3.IsValid);
    }
}
