using Bastet.Data;
using Bastet.Models;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;

namespace Bastet.Tests.HostIpManagement;

/// <summary>
/// Tests for the HostIpValidationService
/// </summary>
public class HostIpValidationTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly IpUtilityService _ipUtilityService;
    private readonly HostIpValidationService _validationService;

    public HostIpValidationTests()
    {
        // Create in-memory database context
        _context = TestDbContextFactory.CreateDbContext();

        // Create services
        _ipUtilityService = new IpUtilityService();
        _validationService = new HostIpValidationService(_ipUtilityService, _context);

        // Set up test data
        SeedTestData();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SeedTestData()
    {
        // Create a test hierarchy of subnets

        // Empty subnet (10.1.0.0/24)
        Subnet emptySubnet = new()
        {
            Id = 1,
            Name = "Empty Subnet",
            NetworkAddress = "10.1.0.0",
            Cidr = 24,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.Subnets.Add(emptySubnet);

        // Subnet with host IPs (10.2.0.0/24)
        Subnet subnetWithHostIps = new()
        {
            Id = 2,
            Name = "Subnet With Host IPs",
            NetworkAddress = "10.2.0.0",
            Cidr = 24,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.Subnets.Add(subnetWithHostIps);

        // Add host IPs to the subnet
        HostIpAssignment hostIp1 = new()
        {
            IP = "10.2.0.5",
            Name = "Host 1",
            SubnetId = 2,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.HostIpAssignments.Add(hostIp1);

        HostIpAssignment hostIp2 = new()
        {
            IP = "10.2.0.10",
            Name = "Host 2",
            SubnetId = 2,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.HostIpAssignments.Add(hostIp2);

        // Parent subnet with child subnets (10.3.0.0/16)
        Subnet parentSubnet = new()
        {
            Id = 3,
            Name = "Parent Subnet",
            NetworkAddress = "10.3.0.0",
            Cidr = 16,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.Subnets.Add(parentSubnet);

        // Child subnet 1 (10.3.1.0/24)
        Subnet childSubnet1 = new()
        {
            Id = 4,
            Name = "Child Subnet 1",
            NetworkAddress = "10.3.1.0",
            Cidr = 24,
            ParentSubnetId = 3,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.Subnets.Add(childSubnet1);

        // Child subnet 2 (10.3.2.0/24)
        Subnet childSubnet2 = new()
        {
            Id = 5,
            Name = "Child Subnet 2",
            NetworkAddress = "10.3.2.0",
            Cidr = 24,
            ParentSubnetId = 3,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.Subnets.Add(childSubnet2);

        // Fully allocated subnet (10.4.0.0/24)
        Subnet fullyAllocatedSubnet = new()
        {
            Id = 6,
            Name = "Fully Allocated Subnet",
            NetworkAddress = "10.4.0.0",
            Cidr = 24,
            IsFullyAllocated = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.Subnets.Add(fullyAllocatedSubnet);

        // Subnet with boundary host IPs (10.5.0.0/24)
        Subnet boundarySubnet = new()
        {
            Id = 7,
            Name = "Boundary Subnet",
            NetworkAddress = "10.5.0.0",
            Cidr = 24,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.Subnets.Add(boundarySubnet);

        // Add host IPs at the boundaries
        HostIpAssignment boundaryHostIp1 = new()
        {
            IP = "10.5.0.1", // First usable IP
            Name = "Boundary Host 1",
            SubnetId = 7,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.HostIpAssignments.Add(boundaryHostIp1);

        HostIpAssignment boundaryHostIp2 = new()
        {
            IP = "10.5.0.254", // Last usable IP
            Name = "Boundary Host 2",
            SubnetId = 7,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.HostIpAssignments.Add(boundaryHostIp2);

        _context.SaveChanges();
    }

    [Fact]
    public void ValidateNewHostIp_WithinValidSubnet_Succeeds()
    {
        // Arrange
        string ip = "10.1.0.100";
        int subnetId = 1; // Empty subnet

        // Act
        ValidationResult result = _validationService.ValidateNewHostIp(ip, subnetId);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateNewHostIp_OutsideSubnetRange_Fails()
    {
        // Arrange
        string ip = "192.168.1.100"; // IP outside of subnet range
        int subnetId = 1; // Empty subnet with range 10.1.0.0/24

        // Act
        ValidationResult result = _validationService.ValidateNewHostIp(ip, subnetId);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("outside the subnet range"));
    }

    [Fact]
    public void ValidateNewHostIp_DuplicateIp_Fails()
    {
        // Arrange
        string ip = "10.2.0.5"; // Already exists in subnet 2
        int subnetId = 2;

        // Act
        ValidationResult result = _validationService.ValidateNewHostIp(ip, subnetId);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("already assigned"));
    }

    [Fact]
    public void ValidateNewHostIp_SubnetWithChildren_Fails()
    {
        // Arrange
        string ip = "10.3.0.100";
        int subnetId = 3; // Parent subnet with child subnets

        // Act
        ValidationResult result = _validationService.ValidateNewHostIp(ip, subnetId);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("child subnets"));
    }

    [Fact]
    public void ValidateNewHostIp_FullyAllocatedSubnet_Fails()
    {
        // Arrange
        string ip = "10.4.0.100";
        int subnetId = 6; // Fully allocated subnet

        // Act
        ValidationResult result = _validationService.ValidateNewHostIp(ip, subnetId);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("fully allocated"));
    }

    [Fact]
    public void ValidateNewHostIp_NetworkAddress_Fails()
    {
        // Arrange
        string ip = "10.1.0.0"; // Network address
        int subnetId = 1;

        // Act
        ValidationResult result = _validationService.ValidateNewHostIp(ip, subnetId);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("network address"));
    }

    [Fact]
    public void ValidateNewHostIp_BroadcastAddress_Fails()
    {
        // Arrange
        string ip = "10.1.0.255"; // Broadcast address for /24
        int subnetId = 1;

        // Act
        ValidationResult result = _validationService.ValidateNewHostIp(ip, subnetId);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("broadcast address"));
    }

    [Fact]
    public void ValidateSubnetCanHaveHostIp_EmptySubnet_Succeeds()
    {
        // Arrange
        int subnetId = 1; // Empty subnet

        // Act
        ValidationResult result = _validationService.ValidateSubnetCanContainHostIp(subnetId);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetCanHaveHostIp_WithChildSubnets_Fails()
    {
        // Arrange
        int subnetId = 3; // Parent subnet with child subnets

        // Act
        ValidationResult result = _validationService.ValidateSubnetCanContainHostIp(subnetId);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("child subnets"));
    }

    [Fact]
    public void ValidateSubnetCanHaveHostIp_FullyAllocated_Fails()
    {
        // Arrange
        int subnetId = 6; // Fully allocated subnet

        // Act
        ValidationResult result = _validationService.ValidateSubnetCanContainHostIp(subnetId);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("fully allocated"));
    }

    [Fact]
    public void ValidateHostIpDeletion_ExistingIp_Succeeds()
    {
        // Arrange
        string ip = "10.2.0.5"; // Existing host IP

        // Act
        ValidationResult result = _validationService.ValidateHostIpDeletion(ip);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateHostIpDeletion_NonExistentIp_Fails()
    {
        // Arrange
        string ip = "192.168.1.100"; // Non-existent host IP

        // Act
        ValidationResult result = _validationService.ValidateHostIpDeletion(ip);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("not found"));
    }

    [Fact]
    public void ValidateSubnetCanBeFullyAllocated_EmptySubnet_Succeeds()
    {
        // Arrange
        int subnetId = 1; // Empty subnet

        // Act
        ValidationResult result = _validationService.ValidateSubnetCanBeFullyAllocated(subnetId);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetCanBeFullyAllocated_WithChildSubnets_Fails()
    {
        // Arrange
        int subnetId = 3; // Subnet with child subnets

        // Act
        ValidationResult result = _validationService.ValidateSubnetCanBeFullyAllocated(subnetId);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("child subnets"));
    }

    [Fact]
    public void ValidateSubnetCanBeFullyAllocated_WithHostIps_Fails()
    {
        // Arrange
        int subnetId = 2; // Subnet with host IPs

        // Act
        ValidationResult result = _validationService.ValidateSubnetCanBeFullyAllocated(subnetId);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("host IP assignments"));
    }
}
