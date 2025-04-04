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
        // Arrange & Act
        ValidationResult result = _validationService.ValidateSubnetContainment(
            "10.0.0.0", 16,  // Child: 10.0.0.0/16
            "10.0.0.0", 8);  // Parent: 10.0.0.0/8

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetContainment_ChildOutsideParent_ReturnsInvalid()
    {
        // Arrange & Act
        ValidationResult result = _validationService.ValidateSubnetContainment(
            "192.168.0.0", 24,  // Child: 192.168.0.0/24
            "10.0.0.0", 8);     // Parent: 10.0.0.0/8

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "NOT_IN_PARENT_RANGE");
    }

    [Fact]
    public void ValidateSubnetContainment_ChildCidrEqualToParent_ReturnsInvalid()
    {
        // Arrange & Act
        ValidationResult result = _validationService.ValidateSubnetContainment(
            "10.0.0.0", 16,  // Child: 10.0.0.0/16
            "10.0.0.0", 16); // Parent: 10.0.0.0/16

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_CIDR_HIERARCHY");
    }

    [Fact]
    public void ValidateSubnetContainment_ChildCidrSmallerThanParent_ReturnsInvalid()
    {
        // Arrange & Act
        ValidationResult result = _validationService.ValidateSubnetContainment(
            "10.0.0.0", 8,   // Child: 10.0.0.0/8
            "10.0.0.0", 16); // Parent: 10.0.0.0/16

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_CIDR_HIERARCHY");
    }

    [Fact]
    public void ValidateSubnetContainment_PartialOverlap_ReturnsInvalid()
    {
        // Arrange & Act
        ValidationResult result = _validationService.ValidateSubnetContainment(
            "10.0.128.0", 17,  // Child: 10.0.128.0/17 (10.0.128.0 - 10.0.255.255)
            "10.0.0.0", 18);   // Parent: 10.0.0.0/18 (10.0.0.0 - 10.0.63.255)

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "NOT_IN_PARENT_RANGE");
    }

    [Fact]
    public void ValidateSubnetContainment_ValidMultiLevelContainment_ReturnsValid()
    {
        // Arrange
        // Validate a multi-level containment (grandparent → parent → child)
        // First check parent in grandparent
        ValidationResult parentInGrandparent = _validationService.ValidateSubnetContainment(
            "10.0.0.0", 16,   // Parent: 10.0.0.0/16
            "10.0.0.0", 8);   // Grandparent: 10.0.0.0/8

        // Then check child in parent
        ValidationResult childInParent = _validationService.ValidateSubnetContainment(
            "10.0.0.0", 24,    // Child: 10.0.0.0/24
            "10.0.0.0", 16);   // Parent: 10.0.0.0/16

        // Finally check child in grandparent (which should also be valid)
        ValidationResult childInGrandparent = _validationService.ValidateSubnetContainment(
            "10.0.0.0", 24,    // Child: 10.0.0.0/24
            "10.0.0.0", 8);    // Grandparent: 10.0.0.0/8

        // Assert
        Assert.True(parentInGrandparent.IsValid);
        Assert.True(childInParent.IsValid);
        Assert.True(childInGrandparent.IsValid);
    }

    [Fact]
    public void ValidateSubnetContainment_EdgeCases_ReturnsExpectedResults()
    {
        // Arrange & Act - Test with maximum differential between parent and child CIDR
        ValidationResult result1 = _validationService.ValidateSubnetContainment(
            "10.0.0.1", 32,  // Child: single IP
            "0.0.0.0", 0);   // Parent: entire Internet

        // Test with valid small differential (just 1)
        ValidationResult result2 = _validationService.ValidateSubnetContainment(
            "192.168.0.0", 25,   // Child: 192.168.0.0/25 (half of the /24)
            "192.168.0.0", 24);  // Parent: 192.168.0.0/24

        // Test with invalid differential due to incorrect alignment
        ValidationResult result3 = _validationService.ValidateSubnetContainment(
            "192.168.1.0", 25,   // Child: 192.168.1.0/25 (not contained in 192.168.0.0/24)
            "192.168.0.0", 24);  // Parent: 192.168.0.0/24

        // Assert
        Assert.True(result1.IsValid);
        Assert.True(result2.IsValid);
        Assert.False(result3.IsValid);
    }
}
