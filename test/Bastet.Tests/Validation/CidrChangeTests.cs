using Bastet.Models;
using Bastet.Services;
using Bastet.Services.Validation;

namespace Bastet.Tests.Validation;

public class CidrChangeTests
{
    private readonly IIpUtilityService _ipUtilityService;
    private readonly SubnetValidationService _validationService;

    public CidrChangeTests()
    {
        _ipUtilityService = new IpUtilityService();
        _validationService = new SubnetValidationService(_ipUtilityService);
    }

    [Fact]
    public void ValidateSubnetCidrChange_NoCidrChange_ReturnsValid()
    {
        // Arrange
        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 24;
        int newCidr = 24; // Same CIDR, no change

        // Act
        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(33)]
    [InlineData(-1)]
    public void ValidateSubnetCidrChange_InvalidCidr_ReturnsInvalid(int invalidCidr)
    {
        // Arrange
        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 24;

        // Act
        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, invalidCidr);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_CIDR_VALUE");
    }

    [Fact]
    public void ValidateSubnetCidrChange_MisalignedNetworkAddress_ReturnsInvalid()
    {
        // Arrange
        int subnetId = 1;
        string networkAddress = "10.0.0.1"; // Not aligned for CIDR 24
        int originalCidr = 32; // Was a host address
        int newCidr = 24;     // Try to change to subnet

        // Act
        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "NETWORK_CIDR_MISMATCH");
    }

    // Tests for decreasing CIDR (making subnet larger)

    [Fact]
    public void ValidateSubnetCidrChange_DecreasingCidr_StaysWithinParent_NoConflicts_ReturnsValid()
    {
        // Arrange
        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 24;
        int newCidr = 22; // Decreasing CIDR (larger subnet)

        Subnet parentSubnet = new() { Id = 2, Name = "Parent", NetworkAddress = "10.0.0.0", Cidr = 16 };
        List<Subnet> siblings =
        [
            new() { Id = 3, Name = "Sibling 1", NetworkAddress = "10.0.4.0", Cidr = 24 },
            new() { Id = 4, Name = "Sibling 2", NetworkAddress = "10.0.8.0", Cidr = 24 }
        ];

        // Act
        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr, parentSubnet, siblings);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetCidrChange_DecreasingCidr_ExpandsBeyondParent_ReturnsInvalid()
    {
        // Arrange
        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 24;
        int newCidr = 15; // Decreasing CIDR beyond parent bounds

        Subnet parentSubnet = new() { Id = 2, Name = "Parent", NetworkAddress = "10.0.0.0", Cidr = 16 };

        // Act
        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr, parentSubnet);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_CIDR_CHANGE");
    }

    [Fact]
    public void ValidateSubnetCidrChange_DecreasingCidr_OverlapsWithSibling_ReturnsInvalid()
    {
        // Arrange
        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 24;
        int newCidr = 23; // Decreasing CIDR will overlap with sibling at 10.0.1.0/24

        Subnet parentSubnet = new() { Id = 2, Name = "Parent", NetworkAddress = "10.0.0.0", Cidr = 16 };
        List<Subnet> siblings =
        [
            new() { Id = 3, Name = "Sibling 1", NetworkAddress = "10.0.1.0", Cidr = 24 }
        ];

        // Act
        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr, parentSubnet, siblings);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SUBNET_OVERLAP");
    }

    [Fact]
    public void ValidateSubnetCidrChange_DecreasingCidr_OverlapsWithUnrelatedSubnet_ReturnsInvalid()
    {
        // Arrange
        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 24;
        int newCidr = 16; // Decreasing CIDR (larger subnet)

        // Other subnet in different branch of hierarchy that would overlap
        List<Subnet> otherSubnets =
        [
            new() { Id = 5, Name = "Different subnet", NetworkAddress = "10.0.128.0", Cidr = 24 }
        ];

        // Act - note we're passing null for parent and siblings, but providing allOtherSubnets
        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr,
            allOtherSubnets: otherSubnets);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SUBNET_OVERLAP");
    }

    [Fact]
    public void ValidateSubnetCidrChange_DecreasingCidr_WithGrandparent_ReturnsValid()
    {
        // 10.0.0.0/8 -> 10.0.0.0/16 -> 10.0.0.0/24, expanding the deepest one to /23. The grandparent
        // contains the expanded subnet, which is the hierarchy working, not an overlap: fitting the
        // direct parent (checked separately) guarantees fitting every ancestor above it.
        Subnet root = new() { Id = 1, Name = "Root", NetworkAddress = "10.0.0.0", Cidr = 8 };
        Subnet parent = new() { Id = 2, Name = "Parent", NetworkAddress = "10.0.0.0", Cidr = 16, ParentSubnetId = 1 };

        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            3, "10.0.0.0", 24, 23, parent, siblings: [], children: [],
            allOtherSubnets: [root, parent]);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetCidrChange_DecreasingCidr_WithGrandchildren_ReturnsValid()
    {
        // The subject 10.0.0.0/24 owns 10.0.0.128/25, which owns 10.0.0.128/26. Expanding the subject
        // to /23 keeps both inside it - a subnet growing can never push out what it already contained.
        Subnet parent = new() { Id = 2, Name = "Parent", NetworkAddress = "10.0.0.0", Cidr = 16 };
        Subnet child = new() { Id = 4, Name = "Child", NetworkAddress = "10.0.0.128", Cidr = 25, ParentSubnetId = 3 };
        Subnet grandchild = new() { Id = 5, Name = "Grandchild", NetworkAddress = "10.0.0.128", Cidr = 26, ParentSubnetId = 4 };

        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            3, "10.0.0.0", 24, 23, parent, siblings: [], children: [child],
            allOtherSubnets: [parent, child, grandchild]);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetCidrChange_DecreasingCidr_SwallowsSubnetInAnotherBranch_ReturnsInvalid()
    {
        // A subnet that is neither an ancestor nor a descendant must still block the expansion, even
        // when it sits deeper in the tree: here 10.0.1.0/25 belongs to a sibling's subtree and would
        // be swallowed by expanding 10.0.0.0/24 to /23.
        Subnet parent = new() { Id = 1, Name = "Parent", NetworkAddress = "10.0.0.0", Cidr = 16 };
        Subnet siblingChild = new() { Id = 4, Name = "Sibling's child", NetworkAddress = "10.0.1.0", Cidr = 25, ParentSubnetId = 3 };

        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            2, "10.0.0.0", 24, 23, parent, siblings: [], children: [],
            allOtherSubnets: [parent, siblingChild]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SUBNET_OVERLAP");
    }

    // Tests for increasing CIDR (making subnet smaller)

    [Fact]
    public void ValidateSubnetCidrChange_IncreasingCidr_NoChildren_ReturnsValid()
    {
        // Arrange
        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 24;
        int newCidr = 25; // Increasing CIDR (smaller subnet)

        // Act
        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetCidrChange_IncreasingCidr_ChildrenStillFit_ReturnsValid()
    {
        // Arrange
        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 22;
        int newCidr = 23; // Increasing CIDR but children still fit

        List<Subnet> children =
        [
            new() { Id = 3, Name = "Child 1", NetworkAddress = "10.0.0.0", Cidr = 24 },
            new() { Id = 4, Name = "Child 2", NetworkAddress = "10.0.0.128", Cidr = 25 }
        ];

        // Act
        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr, children: children);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetCidrChange_IncreasingCidr_OrphansChildren_ReturnsInvalid()
    {
        // Arrange
        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 23;
        int newCidr = 24; // Increasing CIDR will orphan child at 10.0.1.0/24

        List<Subnet> children =
        [
            new() { Id = 3, Name = "Child 1", NetworkAddress = "10.0.0.0", Cidr = 25 },
            new() { Id = 4, Name = "Child 2", NetworkAddress = "10.0.1.0", Cidr = 24 }
        ];

        // Act
        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr, children: children);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CHILD_SUBNET_OUTSIDE_RANGE");
    }

    [Fact]
    public void ValidateSubnetCidrChange_IncreasingCidr_BoundaryCase_ReturnsValid()
    {
        // Arrange
        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 23;
        int newCidr = 24; // Increasing CIDR to exact size needed for children

        List<Subnet> children =
        [
            new() { Id = 3, Name = "Child 1", NetworkAddress = "10.0.0.0", Cidr = 25 },
            new() { Id = 4, Name = "Child 2", NetworkAddress = "10.0.0.128", Cidr = 25 }
        ];

        // Act
        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr, children: children);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
}
