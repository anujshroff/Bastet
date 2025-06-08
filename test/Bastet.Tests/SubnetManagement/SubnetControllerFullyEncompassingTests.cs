using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Tests.SubnetManagement;

/// <summary>
/// Tests for the behavior of subnets that fully encompass a VNet's address prefix
/// </summary>
public class SubnetControllerFullyEncompassingTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly IUserContextService _userContextService;
    private readonly IIpUtilityService _ipUtilityService;
    private readonly SubnetValidationService _validationService;
    private readonly HostIpValidationService _hostIpValidationService;
    private readonly SubnetController _controller;

    public SubnetControllerFullyEncompassingTests()
    {
        // Use SQLite in-memory database for tests
        DbContextOptions<BastetDbContext> options = new DbContextOptionsBuilder<BastetDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new BastetDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        // Set up services
        _userContextService = ControllerTestHelper.CreateMockUserContextService();
        _ipUtilityService = new IpUtilityService();
        _validationService = new SubnetValidationService(_ipUtilityService);
        _hostIpValidationService = new HostIpValidationService(_ipUtilityService, _context);

        // Create and configure the controller
        _controller = new SubnetController(
            _context,
            _ipUtilityService,
            _validationService,
            _hostIpValidationService,
            _userContextService,
            ControllerTestHelper.CreateMockSubnetLockingService()
        );
        ControllerTestHelper.SetupController(_controller);

        // Setup controller context with HttpContext
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        // Add Referer header for testing (simulating request from Azure Import)
        _controller.HttpContext.Request.Headers.Referer = "https://localhost/Azure/Import/1";

        // Set up test data
        SeedTestData();
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SeedTestData()
    {
        // Create a hierarchy of test subnets

        // Root subnet - no parent
        Subnet rootSubnet = new()
        {
            Id = 1,
            Name = "Root Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 8,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(rootSubnet);

        // Parent subnet - for import testing
        Subnet parentSubnet = new()
        {
            Id = 2,
            Name = "Parent Subnet",
            NetworkAddress = "10.11.0.0",
            Cidr = 24,
            ParentSubnetId = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(parentSubnet);

        // Save all changes
        _context.SaveChanges();
    }

    [Fact]
    public async Task BatchCreate_SubnetFullyEncompassesVNetPrefix_MarksParentAsFullyAllocated()
    {
        // Arrange
        int parentId = 2; // Parent Subnet (10.11.0.0/24)
        string vnetName = "Azure-VNet-1";

        // Create a subnet that fully encompasses the VNet's address prefix
        List<CreateSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Default",
                NetworkAddress = "10.11.0.0",
                Cidr = 24, // Same as parent subnet's CIDR
                Description = "Default subnet",
                Tags = "azure",
                ParentSubnetId = parentId,
                FullyEncompassesVNetPrefix = true // This is the key flag
            }
        ];

        // Act
        IActionResult result = await _controller.BatchCreateChildSubnets(parentId, subnets, vnetName);

        // Assert - the controller returns a redirect when called from the Azure/Import
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(parentId, redirectResult.RouteValues?["id"]);

        // Verify parent subnet was renamed and marked as fully allocated
        Subnet? parentSubnet = await _context.Subnets.FindAsync(parentId);
        Assert.NotNull(parentSubnet);
        Assert.Equal(vnetName, parentSubnet.Name);
        Assert.True(parentSubnet.IsFullyAllocated);

        // Verify description contains information about the encompassing subnet
        Assert.Contains("Default", parentSubnet.Description);
        Assert.Contains("fully allocated", parentSubnet.Description?.ToLower());

        // Verify no child subnets were created
        int childSubnetCount = await _context.Subnets
            .Where(s => s.ParentSubnetId == parentId && s.Id != parentId)
            .CountAsync();
        Assert.Equal(0, childSubnetCount);
    }

    [Fact]
    public async Task BatchCreate_MixedSubnets_HandlesFullyEncompassingCorrectly()
    {
        // Arrange
        int parentId = 2; // Parent Subnet (10.11.0.0/24)
        string vnetName = "Azure-VNet-2";

        // Create a mix of subnets, including one that fully encompasses the VNet prefix
        List<CreateSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Default",
                NetworkAddress = "10.11.0.0",
                Cidr = 24, // Same as parent subnet's CIDR
                Description = "Default subnet",
                Tags = "azure",
                ParentSubnetId = parentId,
                FullyEncompassesVNetPrefix = true // This one fully encompasses the VNet prefix
            },
            new()
            {
                Name = "Subnet1",
                NetworkAddress = "10.11.0.0",
                Cidr = 25, // This one would be a valid child subnet
                Description = "Regular subnet 1",
                Tags = "azure",
                ParentSubnetId = parentId,
                FullyEncompassesVNetPrefix = false
            },
            new()
            {
                Name = "Subnet2",
                NetworkAddress = "10.11.0.128",
                Cidr = 25, // This one would be a valid child subnet
                Description = "Regular subnet 2",
                Tags = "azure",
                ParentSubnetId = parentId,
                FullyEncompassesVNetPrefix = false
            }
        ];

        // Act
        IActionResult result = await _controller.BatchCreateChildSubnets(parentId, subnets, vnetName);

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);

        // Verify parent subnet is renamed and marked as fully allocated
        Subnet? parentSubnet = await _context.Subnets.FindAsync(parentId);
        Assert.NotNull(parentSubnet);
        Assert.Equal(vnetName, parentSubnet.Name);
        Assert.True(parentSubnet.IsFullyAllocated);

        // Verify description contains information about the encompassing subnet
        Assert.Contains("Default", parentSubnet.Description);

        // Verify no child subnets were created - since the first subnet fully encompasses the VNet prefix
        int childSubnetCount = await _context.Subnets
            .Where(s => s.ParentSubnetId == parentId && s.Id != parentId)
            .CountAsync();
        Assert.Equal(0, childSubnetCount);
    }
}
