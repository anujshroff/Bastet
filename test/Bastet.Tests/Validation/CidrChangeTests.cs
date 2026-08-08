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

        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 24;
        int newCidr = 24;

        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(33)]
    [InlineData(-1)]
    public void ValidateSubnetCidrChange_InvalidCidr_ReturnsInvalid(int invalidCidr)
    {

        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 24;

        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, invalidCidr);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_CIDR_VALUE");
    }

    [Fact]
    public void ValidateSubnetCidrChange_MisalignedNetworkAddress_ReturnsInvalid()
    {

        int subnetId = 1;
        string networkAddress = "10.0.0.1";
        int originalCidr = 32;
        int newCidr = 24;

        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "NETWORK_CIDR_MISMATCH");
    }

    [Fact]
    public void ValidateSubnetCidrChange_DecreasingCidr_StaysWithinParent_NoConflicts_ReturnsValid()
    {

        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 24;
        int newCidr = 22;

        Subnet parentSubnet = new() { Id = 2, Name = "Parent", NetworkAddress = "10.0.0.0", Cidr = 16 };
        List<Subnet> siblings =
        [
            new() { Id = 3, Name = "Sibling 1", NetworkAddress = "10.0.4.0", Cidr = 24 },
            new() { Id = 4, Name = "Sibling 2", NetworkAddress = "10.0.8.0", Cidr = 24 }
        ];

        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr, parentSubnet, siblings);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetCidrChange_DecreasingCidr_ExpandsBeyondParent_ReturnsInvalid()
    {

        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 24;
        int newCidr = 15;

        Subnet parentSubnet = new() { Id = 2, Name = "Parent", NetworkAddress = "10.0.0.0", Cidr = 16 };

        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr, parentSubnet);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_CIDR_CHANGE");
    }

    [Fact]
    public void ValidateSubnetCidrChange_DecreasingCidr_OverlapsWithSibling_ReturnsInvalid()
    {

        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 24;
        int newCidr = 23;

        Subnet parentSubnet = new() { Id = 2, Name = "Parent", NetworkAddress = "10.0.0.0", Cidr = 16 };
        List<Subnet> siblings =
        [
            new() { Id = 3, Name = "Sibling 1", NetworkAddress = "10.0.1.0", Cidr = 24 }
        ];

        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr, parentSubnet, siblings);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SUBNET_OVERLAP");
    }

    [Fact]
    public void ValidateSubnetCidrChange_DecreasingCidr_OverlapsWithUnrelatedSubnet_ReturnsInvalid()
    {

        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 24;
        int newCidr = 16;

        List<Subnet> otherSubnets =
        [
            new() { Id = 5, Name = "Different subnet", NetworkAddress = "10.0.128.0", Cidr = 24 }
        ];

        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr,
            allOtherSubnets: otherSubnets);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SUBNET_OVERLAP");
    }

    [Fact]
    public void ValidateSubnetCidrChange_DecreasingCidr_WithGrandparent_ReturnsValid()
    {

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

        Subnet parent = new() { Id = 1, Name = "Parent", NetworkAddress = "10.0.0.0", Cidr = 16 };
        Subnet siblingChild = new() { Id = 4, Name = "Sibling's child", NetworkAddress = "10.0.1.0", Cidr = 25, ParentSubnetId = 3 };

        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            2, "10.0.0.0", 24, 23, parent, siblings: [], children: [],
            allOtherSubnets: [parent, siblingChild]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SUBNET_OVERLAP");
    }

    [Fact]
    public void ValidateSubnetCidrChange_IncreasingCidr_NoChildren_ReturnsValid()
    {

        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 24;
        int newCidr = 25;

        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetCidrChange_IncreasingCidr_ChildrenStillFit_ReturnsValid()
    {

        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 22;
        int newCidr = 23;

        List<Subnet> children =
        [
            new() { Id = 3, Name = "Child 1", NetworkAddress = "10.0.0.0", Cidr = 24 },
            new() { Id = 4, Name = "Child 2", NetworkAddress = "10.0.0.128", Cidr = 25 }
        ];

        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr, children: children);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetCidrChange_IncreasingCidr_OrphansChildren_ReturnsInvalid()
    {

        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 23;
        int newCidr = 24;

        List<Subnet> children =
        [
            new() { Id = 3, Name = "Child 1", NetworkAddress = "10.0.0.0", Cidr = 25 },
            new() { Id = 4, Name = "Child 2", NetworkAddress = "10.0.1.0", Cidr = 24 }
        ];

        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr, children: children);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CHILD_SUBNET_OUTSIDE_RANGE");
    }

    [Fact]
    public void ValidateSubnetCidrChange_IncreasingCidr_BoundaryCase_ReturnsValid()
    {

        int subnetId = 1;
        string networkAddress = "10.0.0.0";
        int originalCidr = 23;
        int newCidr = 24;

        List<Subnet> children =
        [
            new() { Id = 3, Name = "Child 1", NetworkAddress = "10.0.0.0", Cidr = 25 },
            new() { Id = 4, Name = "Child 2", NetworkAddress = "10.0.0.128", Cidr = 25 }
        ];

        ValidationResult result = _validationService.ValidateSubnetCidrChange(
            subnetId, networkAddress, originalCidr, newCidr, children: children);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
}
