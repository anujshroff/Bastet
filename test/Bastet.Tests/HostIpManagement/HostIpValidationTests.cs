using Bastet.Data;
using Bastet.Models;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;

namespace Bastet.Tests.HostIpManagement;

public class HostIpValidationTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly IpUtilityService _ipUtilityService;
    private readonly HostIpValidationService _validationService;

    public HostIpValidationTests()
    {

        _context = TestDbContextFactory.CreateDbContext();

        _ipUtilityService = new IpUtilityService();
        _validationService = new HostIpValidationService(_ipUtilityService, _context);

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

        HostIpAssignment boundaryHostIp1 = new()
        {
            IP = "10.5.0.1",
            Name = "Boundary Host 1",
            SubnetId = 7,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.HostIpAssignments.Add(boundaryHostIp1);

        HostIpAssignment boundaryHostIp2 = new()
        {
            IP = "10.5.0.254",
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

        string ip = "10.1.0.100";
        int subnetId = 1;

        ValidationResult result = _validationService.ValidateNewHostIp(ip, subnetId);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateNewHostIp_NetworkOrBroadcastAddressOfNormalSubnet_Fails()
    {

        Assert.Contains(_validationService.ValidateNewHostIp("10.1.0.0", 1).Errors,
            e => e.Code == "NETWORK_ADDRESS_RESERVED");
        Assert.Contains(_validationService.ValidateNewHostIp("10.1.0.255", 1).Errors,
            e => e.Code == "BROADCAST_ADDRESS_RESERVED");
    }

    [Fact]
    public void ValidateNewHostIp_BothAddressesOfSlash31_Succeed()
    {

        Subnet pointToPoint = new()
        {
            Id = 20,
            Name = "P2P Link",
            NetworkAddress = "10.9.0.0",
            Cidr = 31,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.Subnets.Add(pointToPoint);
        _context.SaveChanges();

        Assert.Equal(2, _ipUtilityService.CalculateUsableIpAddresses(31));
        Assert.True(_validationService.ValidateNewHostIp("10.9.0.0", 20).IsValid);
        Assert.True(_validationService.ValidateNewHostIp("10.9.0.1", 20).IsValid);

        Assert.Contains(_validationService.ValidateNewHostIp("10.9.0.2", 20).Errors,
            e => e.Message.Contains("outside the subnet range"));
    }

    [Fact]
    public void ValidateNewHostIp_SingleAddressOfSlash32_Succeeds()
    {

        Subnet singleHost = new()
        {
            Id = 21,
            Name = "Single Host",
            NetworkAddress = "10.9.1.5",
            Cidr = 32,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.Subnets.Add(singleHost);
        _context.SaveChanges();

        Assert.Equal(1, _ipUtilityService.CalculateUsableIpAddresses(32));
        Assert.True(_validationService.ValidateNewHostIp("10.9.1.5", 21).IsValid);
        Assert.Contains(_validationService.ValidateNewHostIp("10.9.1.6", 21).Errors,
            e => e.Message.Contains("outside the subnet range"));
    }

    [Fact]
    public void ValidateNewHostIp_OutsideSubnetRange_Fails()
    {

        string ip = "192.168.1.100";
        int subnetId = 1;

        ValidationResult result = _validationService.ValidateNewHostIp(ip, subnetId);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("outside the subnet range"));
    }

    [Fact]
    public void ValidateNewHostIp_DuplicateIp_Fails()
    {

        string ip = "10.2.0.5";
        int subnetId = 2;

        ValidationResult result = _validationService.ValidateNewHostIp(ip, subnetId);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("already assigned"));
    }

    [Fact]
    public void ValidateNewHostIp_SubnetWithChildren_Fails()
    {

        string ip = "10.3.0.100";
        int subnetId = 3;

        ValidationResult result = _validationService.ValidateNewHostIp(ip, subnetId);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("child subnets"));
    }

    [Fact]
    public void ValidateNewHostIp_FullyAllocatedSubnet_Fails()
    {

        string ip = "10.4.0.100";
        int subnetId = 6;

        ValidationResult result = _validationService.ValidateNewHostIp(ip, subnetId);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("fully allocated"));
    }

    [Fact]
    public void ValidateNewHostIp_NetworkAddress_Fails()
    {

        string ip = "10.1.0.0";
        int subnetId = 1;

        ValidationResult result = _validationService.ValidateNewHostIp(ip, subnetId);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("network address"));
    }

    [Fact]
    public void ValidateNewHostIp_BroadcastAddress_Fails()
    {

        string ip = "10.1.0.255";
        int subnetId = 1;

        ValidationResult result = _validationService.ValidateNewHostIp(ip, subnetId);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("broadcast address"));
    }

    [Fact]
    public void ValidateSubnetCanHaveHostIp_EmptySubnet_Succeeds()
    {

        int subnetId = 1;

        ValidationResult result = _validationService.ValidateSubnetCanContainHostIp(subnetId);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetCanHaveHostIp_WithChildSubnets_Fails()
    {

        int subnetId = 3;

        ValidationResult result = _validationService.ValidateSubnetCanContainHostIp(subnetId);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("child subnets"));
    }

    [Fact]
    public void ValidateSubnetCanHaveHostIp_FullyAllocated_Fails()
    {

        int subnetId = 6;

        ValidationResult result = _validationService.ValidateSubnetCanContainHostIp(subnetId);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("fully allocated"));
    }

    [Fact]
    public void ValidateHostIpDeletion_ExistingIp_Succeeds()
    {

        string ip = "10.2.0.5";

        ValidationResult result = _validationService.ValidateHostIpDeletion(ip);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateHostIpDeletion_NonExistentIp_Fails()
    {

        string ip = "192.168.1.100";

        ValidationResult result = _validationService.ValidateHostIpDeletion(ip);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("not found"));
    }

    [Fact]
    public void ValidateSubnetCanBeFullyAllocated_EmptySubnet_Succeeds()
    {

        int subnetId = 1;

        ValidationResult result = _validationService.ValidateSubnetCanBeFullyAllocated(subnetId);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSubnetCanBeFullyAllocated_WithChildSubnets_Fails()
    {

        int subnetId = 3;

        ValidationResult result = _validationService.ValidateSubnetCanBeFullyAllocated(subnetId);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("child subnets"));
    }

    [Fact]
    public void ValidateSubnetCanBeFullyAllocated_WithHostIps_Fails()
    {

        int subnetId = 2;

        ValidationResult result = _validationService.ValidateSubnetCanBeFullyAllocated(subnetId);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("host IP assignments"));
    }
}
