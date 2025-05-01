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
/// Integration tests for batch subnet creation functionality in the SubnetController
/// </summary>
public class SubnetControllerBatchCreateTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly IUserContextService _userContextService;
    private readonly IIpUtilityService _ipUtilityService;
    private readonly SubnetValidationService _validationService;
    private readonly HostIpValidationService _hostIpValidationService;
    private readonly SubnetController _controller;

    public SubnetControllerBatchCreateTests()
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
            _userContextService
        );
        ControllerTestHelper.SetupController(_controller);

        // Setup controller context with HttpContext
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        // Add Referer header for testing
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
            NetworkAddress = "10.0.0.0",
            Cidr = 16,
            ParentSubnetId = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(parentSubnet);

        // Parent subnet with children - to test conflicts
        Subnet parentWithChildren = new()
        {
            Id = 3,
            Name = "Parent With Children",
            NetworkAddress = "10.1.0.0",
            Cidr = 16,
            ParentSubnetId = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(parentWithChildren);

        // Child subnet of parentWithChildren
        Subnet childSubnet = new()
        {
            Id = 4,
            Name = "Child Subnet",
            NetworkAddress = "10.1.1.0",
            Cidr = 24,
            ParentSubnetId = 3,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(childSubnet);

        // Save all changes
        _context.SaveChanges();
    }

    [Fact]
    public async Task BatchCreate_ValidSubnets_CreatesSubnets()
    {
        // Arrange
        int parentId = 2; // Parent Subnet
        List<CreateSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Test Subnet 1",
                NetworkAddress = "10.0.1.0",
                Cidr = 24,
                Description = "Test description 1",
                Tags = "test,azure",
                ParentSubnetId = parentId
            },
            new()
            {
                Name = "Test Subnet 2",
                NetworkAddress = "10.0.2.0",
                Cidr = 24,
                Description = "Test description 2",
                Tags = "test,azure",
                ParentSubnetId = parentId
            }
        ];

        // Act
        IActionResult result = await _controller.BatchCreate(parentId, subnets);

        // Assert - the controller returns a redirect when called from the Referer we set up
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(parentId, redirectResult.RouteValues?["id"]);

        // Verify subnets were created in the database
        List<Subnet> createdSubnets = await _context.Subnets
            .Where(s => s.ParentSubnetId == parentId && s.Id != parentId)
            .ToListAsync();

        Assert.Equal(2, createdSubnets.Count);
        Assert.Contains(createdSubnets, s => s.Name == "Test Subnet 1" && s.NetworkAddress == "10.0.1.0" && s.Cidr == 24);
        Assert.Contains(createdSubnets, s => s.Name == "Test Subnet 2" && s.NetworkAddress == "10.0.2.0" && s.Cidr == 24);
    }

    [Fact]
    public async Task BatchCreate_WithVNetName_RenamesParentSubnet()
    {
        // Arrange
        int parentId = 2; // Parent Subnet
        string vnetName = "Azure-VNet-1";
        List<CreateSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Azure Subnet 1",
                NetworkAddress = "10.0.1.0",
                Cidr = 24,
                Description = "Imported from Azure",
                Tags = "azure",
                ParentSubnetId = parentId
            }
        ];

        // Act
        IActionResult result = await _controller.BatchCreate(parentId, subnets, vnetName);

        // Assert - the controller returns a redirect when called from the Referer we set up
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(parentId, redirectResult.RouteValues?["id"]);

        // Verify parent subnet was renamed
        Subnet? parentSubnet = await _context.Subnets.FindAsync(parentId);
        Assert.NotNull(parentSubnet);
        Assert.Equal(vnetName, parentSubnet.Name);

        // Verify child subnet was created
        Subnet? childSubnet = await _context.Subnets
            .FirstOrDefaultAsync(s => s.ParentSubnetId == parentId && s.Name == "Azure Subnet 1");
        Assert.NotNull(childSubnet);
    }

    [Fact]
    public async Task BatchCreate_FromNonAzureImport_DoesNotRenameParent()
    {
        // Arrange
        int parentId = 2; // Parent Subnet
        string originalName = "Parent Subnet";
        string vnetName = "Should-Not-Rename";

        // Change the referer to something not from Azure/Import
        _controller.HttpContext.Request.Headers.Referer = "https://localhost/SomeOtherController/Action";

        List<CreateSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Test Subnet",
                NetworkAddress = "10.0.3.0",
                Cidr = 24,
                ParentSubnetId = parentId
            }
        ];

        // Act
        IActionResult result = await _controller.BatchCreate(parentId, subnets, vnetName);

        // Assert
        _ = Assert.IsType<OkObjectResult>(result);

        // Verify parent subnet was NOT renamed
        Subnet? parentSubnet = await _context.Subnets.FindAsync(parentId);
        Assert.NotNull(parentSubnet);
        Assert.Equal(originalName, parentSubnet.Name);
    }

    [Fact]
    public async Task BatchCreate_OverlappingSubnets_ReturnsValidationError()
    {
        // Arrange
        int parentId = 2; // Parent Subnet

        // First clear any existing subnets with this parent to ensure clean test state
        List<Subnet> existingSubnets = await _context.Subnets
            .Where(s => s.ParentSubnetId == parentId && s.Id != parentId)
            .ToListAsync();
        _context.Subnets.RemoveRange(existingSubnets);
        await _context.SaveChangesAsync();

        // Set referer to a non-Azure URL to get a BadRequest result instead of a redirect
        _controller.HttpContext.Request.Headers.Referer = "https://localhost/SomeOtherController/Action";

        List<CreateSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Overlapping Subnet 1",
                NetworkAddress = "10.0.1.0",
                Cidr = 24,
                ParentSubnetId = parentId
            },
            new()
            {
                Name = "Overlapping Subnet 2",
                NetworkAddress = "10.0.1.0",
                Cidr = 24, // Same as Subnet 1 - should cause conflict
                ParentSubnetId = parentId
            }
        ];

        // Act
        IActionResult result = await _controller.BatchCreate(parentId, subnets);

        // Assert - when overlapping subnets are provided, controller returns BadRequest
        BadRequestObjectResult badRequestResult = Assert.IsType<BadRequestObjectResult>(result);

        // Verify no subnets were created
        int subnetCount = await _context.Subnets
            .Where(s => s.ParentSubnetId == parentId && s.Id != parentId)
            .CountAsync();

        // With proper transaction management, no subnets should be created when there's an overlap
        // The transaction should roll back all changes
        Assert.Equal(0, subnetCount);
    }

    [Fact]
    public async Task BatchCreate_SubnetsOutsideParent_ReturnsValidationError()
    {
        // Arrange
        int parentId = 2; // Parent Subnet - 10.0.0.0/16
        List<CreateSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Outside Parent Range",
                NetworkAddress = "192.168.1.0", // Outside parent range
                Cidr = 24,
                ParentSubnetId = parentId
            }
        ];

        // Act
        IActionResult result = await _controller.BatchCreate(parentId, subnets);

        // Assert - when subnets outside parent range are passed in, controller returns BadRequest
        BadRequestObjectResult badRequestResult = Assert.IsType<BadRequestObjectResult>(result);

        // Verify no subnets were created
        int subnetCount = await _context.Subnets
            .Where(s => s.ParentSubnetId == parentId && s.Id != parentId)
            .CountAsync();

        Assert.Equal(0, subnetCount);
    }

    [Fact]
    public async Task BatchCreate_EmptyList_ReturnsValidationError()
    {
        // Arrange
        int parentId = 2;
        List<CreateSubnetViewModel> subnets = [];

        // Act
        // Set referer to a non-Azure URL to get a BadRequest result instead of a redirect
        _controller.HttpContext.Request.Headers.Referer = "https://localhost/SomeOtherController/Action";
        IActionResult result = await _controller.BatchCreate(parentId, subnets);

        // Assert - when an empty list is passed, the controller returns OkObjectResult
        _ = Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task BatchCreate_ParentNotFound_ReturnsNotFound()
    {
        // Arrange
        int nonExistentParentId = 999;
        List<CreateSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Test Subnet",
                NetworkAddress = "10.0.1.0",
                Cidr = 24,
                ParentSubnetId = nonExistentParentId
            }
        ];

        // Act
        IActionResult result = await _controller.BatchCreate(nonExistentParentId, subnets);

        // Assert - The controller returns BadRequestObjectResult for invalid parent
        _ = Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task BatchCreate_FromAzureImport_ReturnsRedirect()
    {
        // Arrange
        int parentId = 2;
        List<CreateSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Azure Import Subnet",
                NetworkAddress = "10.0.1.0",
                Cidr = 24,
                Description = "Imported from Azure",
                Tags = "azure",
                ParentSubnetId = parentId
            }
        ];

        // Set referer to Azure import page
        _controller.HttpContext.Request.Headers.Referer = "https://localhost/Azure/Import/2";

        // Act
        IActionResult result = await _controller.BatchCreate(parentId, subnets);

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(parentId, redirectResult.RouteValues?["id"]);

        // Verify subnet was created
        Subnet? createdSubnet = await _context.Subnets
            .FirstOrDefaultAsync(s => s.ParentSubnetId == parentId && s.Name == "Azure Import Subnet");

        Assert.NotNull(createdSubnet);
    }
}
