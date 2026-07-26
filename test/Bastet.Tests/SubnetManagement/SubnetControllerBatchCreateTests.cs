using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Security;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

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
            _userContextService,
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<SubnetController>.Instance
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
    public async Task BatchCreateChildSubnets_ValidSubnets_CreatesSubnets()
    {
        // Arrange
        int parentId = 2; // Parent Subnet
        List<AzureImportSubnetViewModel> subnets =
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
        IActionResult result = await _controller.BatchCreateChildSubnets(parentId, subnets, isAzureImport: true);

        // Assert - the controller redirects when the caller declares this is an Azure import
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(parentId, redirectResult.RouteValues?["id"]);

        // Verify subnets were created in the database
        List<Subnet> createdSubnets = await _context.Subnets
            .Where(s => s.ParentSubnetId == parentId && s.Id != parentId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, createdSubnets.Count);
        Assert.Contains(createdSubnets, s => s.Name == "Test Subnet 1" && s.NetworkAddress == "10.0.1.0" && s.Cidr == 24);
        Assert.Contains(createdSubnets, s => s.Name == "Test Subnet 2" && s.NetworkAddress == "10.0.2.0" && s.Cidr == 24);
    }

    [Theory]
    // 54 characters: a realistic Azure VNet name, which the 100-character column keeps whole.
    [InlineData("corporate-network-westeurope-production-environment-01", 54)]
    // Azure's own limit is 64 characters, still comfortably inside the column.
    [InlineData("corporate-network-westeurope-production-environment-01-secondary", 64)]
    public async Task BatchCreateChildSubnets_WithLongVNetName_KeepsTheWholeName(string vnetName, int expectedLength)
    {
        int parentId = 2; // Parent Subnet
        Assert.Equal(expectedLength, vnetName.Length);

        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {
                Name = "web",
                NetworkAddress = "10.0.1.0",
                Cidr = 24,
                ParentSubnetId = parentId
            }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(
            parentId, subnets, vnetName: vnetName, isAzureImport: true);

        _ = Assert.IsType<RedirectToActionResult>(result);

        _context.ChangeTracker.Clear();
        Subnet parent = (await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken))!;
        Assert.Equal(vnetName, parent.Name);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_WithVNetNameBeyondTheColumn_TruncatesToColumnLength()
    {
        // Beyond any real Azure name, so this only guards a hand-crafted post: the value still has to
        // fit Subnet.Name rather than fail the insert with a SQL truncation error.
        int parentId = 2; // Parent Subnet
        string vnetName = new('v', 150);

        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {
                Name = "web",
                NetworkAddress = "10.0.1.0",
                Cidr = 24,
                ParentSubnetId = parentId
            }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(
            parentId, subnets, vnetName: vnetName, isAzureImport: true);

        _ = Assert.IsType<RedirectToActionResult>(result);

        _context.ChangeTracker.Clear();
        Subnet parent = (await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken))!;
        Assert.Equal(100, parent.Name.Length);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_WithLongAzureSubnetName_ImportsItWhole()
    {
        // Azure subnet names go up to 80 characters. While Subnet.Name held 50, the inherited
        // [StringLength] rejected these during model binding - before the action ran - so the import
        // returned raw ModelState JSON to a full-page form post and nothing could be imported.
        int parentId = 2; // Parent Subnet
        string azureName = new('s', 80);

        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {
                Name = azureName,
                NetworkAddress = "10.0.1.0",
                Cidr = 24,
                ParentSubnetId = parentId
            }
        ];

        // The length limit lives in a validation attribute, which only runs during model binding.
        List<System.ComponentModel.DataAnnotations.ValidationResult> validationResults = [];
        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            subnets[0],
            new System.ComponentModel.DataAnnotations.ValidationContext(subnets[0]),
            validationResults,
            validateAllProperties: true);
        Assert.DoesNotContain(validationResults, v => v.MemberNames.Contains("Name"));

        IActionResult result = await _controller.BatchCreateChildSubnets(
            parentId, subnets, vnetName: "vnet-production", isAzureImport: true);

        _ = Assert.IsType<RedirectToActionResult>(result);
        Subnet created = Assert.Single(
            await _context.Subnets.Where(s => s.NetworkAddress == "10.0.1.0" && s.Cidr == 24)
                .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(azureName, created.Name);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_WithOverLongAzureResourceId_IsRejectedAndStoresNothing()
    {
        // Sanitization trims resource IDs at 1000 while the column holds 500, so an over-long value
        // used to reach the insert and fail it behind a generic 500. Rejected rather than truncated:
        // reconcile matches subnets to live Azure by this ID, so a shortened one would report the
        // subnet as deleted in Azure permanently.
        int parentId = 2; // Parent Subnet
        int subnetCountBefore = await _context.Subnets.CountAsync(TestContext.Current.CancellationToken);

        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {
                Name = "web",
                NetworkAddress = "10.0.1.0",
                Cidr = 24,
                ParentSubnetId = parentId,
                AzureResourceId = "/subscriptions/" + new string('x', 600)
            }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(
            parentId, subnets, vnetName: "vnet-production", isAzureImport: true,
            sanitizationService: new InputSanitizationService());

        _ = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(subnetCountBefore, await _context.Subnets.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BatchCreateChildSubnets_WithRealisticAzureResourceId_ImportsIt()
    {
        // A full-length ARM ID for a subnet is roughly 330 characters, comfortably inside the column.
        int parentId = 2; // Parent Subnet
        string resourceId =
            "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/" + new string('r', 80) +
            "/providers/Microsoft.Network/virtualNetworks/" + new string('v', 64) + "/subnets/" + new string('s', 80);
        Assert.InRange(resourceId.Length, 300, 500);

        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {
                Name = "web",
                NetworkAddress = "10.0.1.0",
                Cidr = 24,
                ParentSubnetId = parentId,
                AzureResourceId = resourceId
            }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(
            parentId, subnets, vnetName: "vnet-production", isAzureImport: true,
            sanitizationService: new InputSanitizationService());

        _ = Assert.IsType<RedirectToActionResult>(result);
        Subnet created = Assert.Single(
            await _context.Subnets.Where(s => s.Name == "web").ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(resourceId, created.AzureResourceId);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_WithAzureResourceId_PersistsItOnTheCreatedSubnet()
    {
        // The import wizard posts the Azure resource ID with each subnet; reconcile and the portal
        // link both depend on it landing on the entity.
        int parentId = 2; // Parent Subnet
        string resourceId = "/subscriptions/test/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-a/subnets/web";
        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Imported Subnet",
                NetworkAddress = "10.0.1.0",
                Cidr = 24,
                ParentSubnetId = parentId,
                AzureResourceId = resourceId
            }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(parentId, subnets, isAzureImport: true);

        _ = Assert.IsType<RedirectToActionResult>(result);
        Subnet created = Assert.Single(
            await _context.Subnets.Where(s => s.Name == "Imported Subnet").ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(resourceId, created.AzureResourceId);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_WithVNetName_RenamesParentSubnet()
    {
        // Arrange
        int parentId = 2; // Parent Subnet
        string vnetName = "Azure-VNet-1";
        List<AzureImportSubnetViewModel> subnets =
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
        IActionResult result = await _controller.BatchCreateChildSubnets(parentId, subnets, vnetName, isAzureImport: true);

        // Assert - the controller redirects when the caller declares this is an Azure import
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(parentId, redirectResult.RouteValues?["id"]);

        // Verify parent subnet was renamed
        Subnet? parentSubnet = await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken);
        Assert.NotNull(parentSubnet);
        Assert.Equal(vnetName, parentSubnet.Name);

        // Verify child subnet was created
        Subnet? childSubnet = await _context.Subnets
            .FirstOrDefaultAsync(s => s.ParentSubnetId == parentId && s.Name == "Azure Subnet 1", TestContext.Current.CancellationToken);
        Assert.NotNull(childSubnet);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_FromNonAzureImport_DoesNotRenameParent()
    {
        // Arrange
        int parentId = 2; // Parent Subnet
        string originalName = "Parent Subnet";
        string vnetName = "Should-Not-Rename";

        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Test Subnet",
                NetworkAddress = "10.0.3.0",
                Cidr = 24,
                ParentSubnetId = parentId
            }
        ];

        // Act - isAzureImport defaults to false, so this is a plain batch create
        IActionResult result = await _controller.BatchCreateChildSubnets(parentId, subnets, vnetName);

        // Assert
        _ = Assert.IsType<OkObjectResult>(result);

        // Verify parent subnet was NOT renamed
        Subnet? parentSubnet = await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken);
        Assert.NotNull(parentSubnet);
        Assert.Equal(originalName, parentSubnet.Name);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_OverlappingSubnets_ReturnsValidationError()
    {
        // Arrange
        int parentId = 2; // Parent Subnet

        // First clear any existing subnets with this parent to ensure clean test state
        List<Subnet> existingSubnets = await _context.Subnets
            .Where(s => s.ParentSubnetId == parentId && s.Id != parentId)
            .ToListAsync(TestContext.Current.CancellationToken);
        _context.Subnets.RemoveRange(existingSubnets);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Set referer to a non-Azure URL to get a BadRequest result instead of a redirect
        _controller.HttpContext.Request.Headers.Referer = "https://localhost/SomeOtherController/Action";

        List<AzureImportSubnetViewModel> subnets =
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
        IActionResult result = await _controller.BatchCreateChildSubnets(parentId, subnets);

        // Assert - when overlapping subnets are provided, controller returns BadRequest
        BadRequestObjectResult badRequestResult = Assert.IsType<BadRequestObjectResult>(result);

        // Verify no subnets were created
        int subnetCount = await _context.Subnets
            .Where(s => s.ParentSubnetId == parentId && s.Id != parentId)
            .CountAsync(TestContext.Current.CancellationToken);

        // With proper transaction management, no subnets should be created when there's an overlap
        // The transaction should roll back all changes
        Assert.Equal(0, subnetCount);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_SubnetsOutsideParent_ReturnsValidationError()
    {
        // Arrange
        int parentId = 2; // Parent Subnet - 10.0.0.0/16
        List<AzureImportSubnetViewModel> subnets =
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
        IActionResult result = await _controller.BatchCreateChildSubnets(parentId, subnets);

        // Assert - when subnets outside parent range are passed in, controller returns BadRequest
        BadRequestObjectResult badRequestResult = Assert.IsType<BadRequestObjectResult>(result);

        // Verify no subnets were created
        int subnetCount = await _context.Subnets
            .Where(s => s.ParentSubnetId == parentId && s.Id != parentId)
            .CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, subnetCount);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_EmptyList_ReturnsValidationError()
    {
        // An empty list means nothing was selected, or nothing bound. It used to fall through to the
        // parent rename and report "imported 0 child subnets" as a success.
        int parentId = 2;
        string originalName = (await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken))!.Name;

        IActionResult result = await _controller.BatchCreateChildSubnets(
            parentId, [], vnetName: "vnet-production", isAzureImport: true);

        _ = Assert.IsType<BadRequestObjectResult>(result);

        // The parent must be left exactly as it was
        _context.ChangeTracker.Clear();
        Subnet parent = (await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken))!;
        Assert.Equal(originalName, parent.Name);
        Assert.Null(parent.AzureResourceId);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_ParentNotFound_ReturnsNotFound()
    {
        // Arrange
        int nonExistentParentId = 999;
        List<AzureImportSubnetViewModel> subnets =
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
        IActionResult result = await _controller.BatchCreateChildSubnets(nonExistentParentId, subnets);

        // Assert - The controller returns BadRequestObjectResult for invalid parent
        _ = Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_FromAzureImport_ReturnsRedirect()
    {
        // Arrange
        int parentId = 2;
        List<AzureImportSubnetViewModel> subnets =
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

        // Act
        IActionResult result = await _controller.BatchCreateChildSubnets(parentId, subnets, isAzureImport: true);

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(parentId, redirectResult.RouteValues?["id"]);

        // Verify subnet was created
        Subnet? createdSubnet = await _context.Subnets
            .FirstOrDefaultAsync(s => s.ParentSubnetId == parentId && s.Name == "Azure Import Subnet", TestContext.Current.CancellationToken);

        Assert.NotNull(createdSubnet);
    }
}
