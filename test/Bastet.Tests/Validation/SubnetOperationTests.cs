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

    // Creation tests

    [Fact]
    public void ValidateNewSubnet_ValidData_ReturnsValid()
    {
        // Arrange
        CreateSubnetDto subnetDto = new()
        {
            Name = "Test Subnet",
            NetworkAddress = "192.168.1.0",
            Cidr = 24,
            Description = "A test subnet"
        };

        // Act
        ValidationResult result = _validationService.ValidateNewSubnet(subnetDto);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateNewSubnet_MissingName_ReturnsInvalid()
    {
        // Arrange
        CreateSubnetDto subnetDto = new()
        {
            Name = "", // Missing name
            NetworkAddress = "192.168.1.0",
            Cidr = 24
        };

        // Act
        ValidationResult result = _validationService.ValidateNewSubnet(subnetDto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "REQUIRED_FIELD_MISSING");
    }

    [Fact]
    public void ValidateNewSubnet_MissingNetworkAddress_ReturnsInvalid()
    {
        // Arrange
        CreateSubnetDto subnetDto = new()
        {
            Name = "Test Subnet",
            NetworkAddress = "", // Missing network address
            Cidr = 24
        };

        // Act
        ValidationResult result = _validationService.ValidateNewSubnet(subnetDto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "REQUIRED_FIELD_MISSING");
    }

    [Fact]
    public void ValidateNewSubnet_InvalidNetworkFormat_ReturnsInvalid()
    {
        // Arrange
        CreateSubnetDto subnetDto = new()
        {
            Name = "Test Subnet",
            NetworkAddress = "invalid-ip",
            Cidr = 24
        };

        // Act
        ValidationResult result = _validationService.ValidateNewSubnet(subnetDto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_NETWORK_FORMAT");
    }

    [Fact]
    public void ValidateNewSubnet_ValidChildInParent_ReturnsValid()
    {
        // Arrange
        CreateSubnetDto subnetDto = new()
        {
            Name = "Child Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 24,
            ParentSubnetId = 1
        };

        Subnet parentSubnet = new()
        {
            Id = 1,
            Name = "Parent Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 16
        };

        // Act
        ValidationResult result = _validationService.ValidateNewSubnet(subnetDto, parentSubnet);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateNewSubnet_ChildOutsideParent_ReturnsInvalid()
    {
        // Arrange
        CreateSubnetDto subnetDto = new()
        {
            Name = "Child Subnet",
            NetworkAddress = "192.168.1.0",
            Cidr = 24,
            ParentSubnetId = 1
        };

        Subnet parentSubnet = new()
        {
            Id = 1,
            Name = "Parent Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 16
        };

        // Act
        ValidationResult result = _validationService.ValidateNewSubnet(subnetDto, parentSubnet);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "NOT_IN_PARENT_RANGE");
    }

    [Fact]
    public void ValidateNewSubnet_ChildCidrSmallerThanParent_ReturnsInvalid()
    {
        // Arrange
        CreateSubnetDto subnetDto = new()
        {
            Name = "Child Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 8, // Smaller CIDR than parent (larger subnet)
            ParentSubnetId = 1
        };

        Subnet parentSubnet = new()
        {
            Id = 1,
            Name = "Parent Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 16
        };

        // Act
        ValidationResult result = _validationService.ValidateNewSubnet(subnetDto, parentSubnet);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_CIDR_HIERARCHY");
    }

    [Fact]
    public void ValidateNewSubnet_OverlapsWithSibling_ReturnsInvalid()
    {
        // Arrange
        CreateSubnetDto subnetDto = new()
        {
            Name = "New Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 24,
            ParentSubnetId = 1
        };

        Subnet parentSubnet = new()
        {
            Id = 1,
            Name = "Parent Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 16
        };

        List<Subnet> siblings =
        [
            new() { Id = 2, Name = "Existing Sibling", NetworkAddress = "10.0.0.0", Cidr = 24 }
        ];

        // Act
        ValidationResult result = _validationService.ValidateNewSubnet(subnetDto, parentSubnet, siblings);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SUBNET_OVERLAP");
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

    // Deletion tests

    [Fact]
    public void ValidateSubnetDeletion_NoChildren_ReturnsValid()
    {
        // Arrange
        Subnet subnet = new()
        {
            Id = 1,
            Name = "Test Subnet",
            NetworkAddress = "192.168.1.0",
            Cidr = 24,
            ChildSubnets = [] // Empty list, no children
        };

        // Act
        ValidationResult result = _validationService.ValidateSubnetDeletion(subnet);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetDeletion_WithChildren_ReturnsInvalid()
    {
        // Arrange
        Subnet childSubnet = new()
        {
            Id = 2,
            Name = "Child Subnet",
            NetworkAddress = "192.168.1.0",
            Cidr = 25,
            ParentSubnetId = 1
        };

        Subnet subnet = new()
        {
            Id = 1,
            Name = "Parent Subnet",
            NetworkAddress = "192.168.1.0",
            Cidr = 24,
            ChildSubnets = [childSubnet]
        };

        // Act
        ValidationResult result = _validationService.ValidateSubnetDeletion(subnet);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SUBNET_HAS_CHILDREN");
    }

    // Update tests

    [Fact]
    public void ValidateSubnetUpdate_ValidData_ReturnsValid()
    {
        // Arrange
        Subnet subnet = new()
        {
            Id = 1,
            Name = "Original Name",
            NetworkAddress = "192.168.1.0",
            Cidr = 24,
            Description = "Original description"
        };

        UpdateSubnetDto updateDto = new()
        {
            Name = "Updated Name",
            Tags = "new-tag",
            Description = "Updated description"
        };

        // Act
        ValidationResult result = _validationService.ValidateSubnetUpdate(subnet, updateDto);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetUpdate_MissingName_ReturnsInvalid()
    {
        // Arrange
        Subnet subnet = new()
        {
            Id = 1,
            Name = "Original Name",
            NetworkAddress = "192.168.1.0",
            Cidr = 24
        };

        UpdateSubnetDto updateDto = new()
        {
            Name = "", // Missing name
            Description = "Updated description"
        };

        // Act
        ValidationResult result = _validationService.ValidateSubnetUpdate(subnet, updateDto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "REQUIRED_FIELD_MISSING");
    }
}
