using Bastet.Models;
using Bastet.Models.DTOs;
using Bastet.Services;
using Bastet.Services.Validation;

namespace Bastet.Tests.Validation;

public class SubnetOperationTests
{
    private readonly IIpUtilityService _ipUtilityService;
    private readonly ISubnetValidationService _validationService;

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
        var subnetDto = new CreateSubnetDto
        {
            Name = "Test Subnet",
            NetworkAddress = "192.168.1.0",
            Cidr = 24,
            Description = "A test subnet"
        };

        // Act
        var result = _validationService.ValidateNewSubnet(subnetDto);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateNewSubnet_MissingName_ReturnsInvalid()
    {
        // Arrange
        var subnetDto = new CreateSubnetDto
        {
            Name = "", // Missing name
            NetworkAddress = "192.168.1.0",
            Cidr = 24
        };

        // Act
        var result = _validationService.ValidateNewSubnet(subnetDto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "REQUIRED_FIELD_MISSING");
    }

    [Fact]
    public void ValidateNewSubnet_MissingNetworkAddress_ReturnsInvalid()
    {
        // Arrange
        var subnetDto = new CreateSubnetDto
        {
            Name = "Test Subnet",
            NetworkAddress = "", // Missing network address
            Cidr = 24
        };

        // Act
        var result = _validationService.ValidateNewSubnet(subnetDto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "REQUIRED_FIELD_MISSING");
    }

    [Fact]
    public void ValidateNewSubnet_InvalidNetworkFormat_ReturnsInvalid()
    {
        // Arrange
        var subnetDto = new CreateSubnetDto
        {
            Name = "Test Subnet",
            NetworkAddress = "invalid-ip",
            Cidr = 24
        };

        // Act
        var result = _validationService.ValidateNewSubnet(subnetDto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_NETWORK_FORMAT");
    }

    [Fact]
    public void ValidateNewSubnet_ValidChildInParent_ReturnsValid()
    {
        // Arrange
        var subnetDto = new CreateSubnetDto
        {
            Name = "Child Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 24,
            ParentSubnetId = 1
        };

        var parentSubnet = new Subnet
        {
            Id = 1,
            Name = "Parent Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 16
        };

        // Act
        var result = _validationService.ValidateNewSubnet(subnetDto, parentSubnet);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateNewSubnet_ChildOutsideParent_ReturnsInvalid()
    {
        // Arrange
        var subnetDto = new CreateSubnetDto
        {
            Name = "Child Subnet",
            NetworkAddress = "192.168.1.0",
            Cidr = 24,
            ParentSubnetId = 1
        };

        var parentSubnet = new Subnet
        {
            Id = 1,
            Name = "Parent Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 16
        };

        // Act
        var result = _validationService.ValidateNewSubnet(subnetDto, parentSubnet);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "NOT_IN_PARENT_RANGE");
    }

    [Fact]
    public void ValidateNewSubnet_ChildCidrSmallerThanParent_ReturnsInvalid()
    {
        // Arrange
        var subnetDto = new CreateSubnetDto
        {
            Name = "Child Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 8, // Smaller CIDR than parent (larger subnet)
            ParentSubnetId = 1
        };

        var parentSubnet = new Subnet
        {
            Id = 1,
            Name = "Parent Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 16
        };

        // Act
        var result = _validationService.ValidateNewSubnet(subnetDto, parentSubnet);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_CIDR_HIERARCHY");
    }

    [Fact]
    public void ValidateNewSubnet_OverlapsWithSibling_ReturnsInvalid()
    {
        // Arrange
        var subnetDto = new CreateSubnetDto
        {
            Name = "New Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 24,
            ParentSubnetId = 1
        };

        var parentSubnet = new Subnet
        {
            Id = 1,
            Name = "Parent Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 16
        };

        var siblings = new List<Subnet>
        {
            new() { Id = 2, Name = "Existing Sibling", NetworkAddress = "10.0.0.0", Cidr = 24 }
        };

        // Act
        var result = _validationService.ValidateNewSubnet(subnetDto, parentSubnet, siblings);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SUBNET_OVERLAP");
    }

    // Deletion tests

    [Fact]
    public void ValidateSubnetDeletion_NoChildren_ReturnsValid()
    {
        // Arrange
        var subnet = new Subnet
        {
            Id = 1,
            Name = "Test Subnet",
            NetworkAddress = "192.168.1.0",
            Cidr = 24,
            ChildSubnets = new List<Subnet>() // Empty list, no children
        };

        // Act
        var result = _validationService.ValidateSubnetDeletion(subnet);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetDeletion_WithChildren_ReturnsInvalid()
    {
        // Arrange
        var childSubnet = new Subnet
        {
            Id = 2,
            Name = "Child Subnet",
            NetworkAddress = "192.168.1.0",
            Cidr = 25,
            ParentSubnetId = 1
        };

        var subnet = new Subnet
        {
            Id = 1,
            Name = "Parent Subnet",
            NetworkAddress = "192.168.1.0",
            Cidr = 24,
            ChildSubnets = new List<Subnet> { childSubnet }
        };

        // Act
        var result = _validationService.ValidateSubnetDeletion(subnet);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SUBNET_HAS_CHILDREN");
    }

    // Update tests

    [Fact]
    public void ValidateSubnetUpdate_ValidData_ReturnsValid()
    {
        // Arrange
        var subnet = new Subnet
        {
            Id = 1,
            Name = "Original Name",
            NetworkAddress = "192.168.1.0",
            Cidr = 24,
            Description = "Original description"
        };

        var updateDto = new UpdateSubnetDto
        {
            Name = "Updated Name",
            Tags = "new-tag",
            Description = "Updated description"
        };

        // Act
        var result = _validationService.ValidateSubnetUpdate(subnet, updateDto);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetUpdate_MissingName_ReturnsInvalid()
    {
        // Arrange
        var subnet = new Subnet
        {
            Id = 1,
            Name = "Original Name",
            NetworkAddress = "192.168.1.0",
            Cidr = 24
        };

        var updateDto = new UpdateSubnetDto
        {
            Name = "", // Missing name
            Description = "Updated description"
        };

        // Act
        var result = _validationService.ValidateSubnetUpdate(subnet, updateDto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "REQUIRED_FIELD_MISSING");
    }
}
