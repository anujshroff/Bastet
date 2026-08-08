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

[Collection(Bastet.Tests.Azure.AzureFeatureFlagCollection.Name)]
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

        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "true");

        DbContextOptions<BastetDbContext> options = new DbContextOptionsBuilder<BastetDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new BastetDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        _userContextService = ControllerTestHelper.CreateMockUserContextService();
        _ipUtilityService = new IpUtilityService();
        _validationService = new SubnetValidationService(_ipUtilityService);
        _hostIpValidationService = new HostIpValidationService(_ipUtilityService, _context);

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

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        _controller.HttpContext.Request.Headers.Referer = "https://localhost/Azure/Import/1";

        SeedTestData();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", null);
        _context.Database.CloseConnection();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SeedTestData()
    {

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

        _context.SaveChanges();
    }

    private void AssertImportFailureRedirect(IActionResult result, int parentId)
    {
        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal(parentId, redirect.RouteValues?["id"]);
        Assert.True(_controller.TempData.ContainsKey("ErrorMessage"));
        Assert.False(string.IsNullOrWhiteSpace(_controller.TempData["ErrorMessage"] as string));
    }

    [Fact]
    public async Task BatchCreateChildSubnets_ValidSubnets_CreatesSubnets()
    {

        int parentId = 2;
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

        IActionResult result = await _controller.BatchCreateChildSubnets(parentId, subnets, isAzureImport: true);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(parentId, redirectResult.RouteValues?["id"]);

        List<Subnet> createdSubnets = await _context.Subnets
            .Where(s => s.ParentSubnetId == parentId && s.Id != parentId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, createdSubnets.Count);
        Assert.Contains(createdSubnets, s => s.Name == "Test Subnet 1" && s.NetworkAddress == "10.0.1.0" && s.Cidr == 24);
        Assert.Contains(createdSubnets, s => s.Name == "Test Subnet 2" && s.NetworkAddress == "10.0.2.0" && s.Cidr == 24);
    }

    [Theory]

    [InlineData("corporate-network-westeurope-production-environment-01", 54)]

    [InlineData("corporate-network-westeurope-production-environment-01-secondary", 64)]
    public async Task BatchCreateChildSubnets_WithLongVNetName_KeepsTheWholeName(string vnetName, int expectedLength)
    {
        int parentId = 2;
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

        int parentId = 2;
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

        int parentId = 2;
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

        int parentId = 2;
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

        AssertImportFailureRedirect(result, parentId);
        Assert.Equal(subnetCountBefore, await _context.Subnets.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BatchCreateChildSubnets_WithRealisticAzureResourceId_ImportsIt()
    {

        int parentId = 2;
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

        int parentId = 2;
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

        int parentId = 2;
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

        IActionResult result = await _controller.BatchCreateChildSubnets(parentId, subnets, vnetName, isAzureImport: true);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(parentId, redirectResult.RouteValues?["id"]);

        Subnet? parentSubnet = await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken);
        Assert.NotNull(parentSubnet);
        Assert.Equal(vnetName, parentSubnet.Name);

        Subnet? childSubnet = await _context.Subnets
            .FirstOrDefaultAsync(s => s.ParentSubnetId == parentId && s.Name == "Azure Subnet 1", TestContext.Current.CancellationToken);
        Assert.NotNull(childSubnet);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_OnAPopulatedTarget_DoesNotRenameTheParent()
    {
        const int ParentId = 2;

        _context.Subnets.Add(new Subnet
        {
            Id = 800,
            Name = "already-imported",
            NetworkAddress = "10.0.9.0",
            Cidr = 24,
            ParentSubnetId = ParentId
        });
        Subnet? parent = await _context.Subnets.FindAsync([ParentId], TestContext.Current.CancellationToken);
        Assert.NotNull(parent);
        parent.Name = "Production Core";
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Azure Subnet Topup",
                NetworkAddress = "10.0.8.0",
                Cidr = 24,
                ParentSubnetId = ParentId
            }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(
            ParentId, subnets, "Azure-VNet-1", isAzureImport: true);

        _ = Assert.IsType<RedirectToActionResult>(result);

        Subnet? after = await _context.Subnets.FindAsync([ParentId], TestContext.Current.CancellationToken);
        Assert.NotNull(after);
        Assert.Equal("Production Core", after.Name);

        string? flash = _controller.TempData["SuccessMessage"] as string;
        Assert.NotNull(flash);
        Assert.DoesNotContain("renamed", flash, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_OnAnEmptyTarget_StillRenamesTheParent()
    {
        const int ParentId = 2;

        Subnet? parent = await _context.Subnets.FindAsync([ParentId], TestContext.Current.CancellationToken);
        Assert.NotNull(parent);
        parent.Name = "Production Core";
        parent.AzureResourceId = null;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        List<AzureImportSubnetViewModel> subnets =
        [
            new() { Name = "Azure Subnet 1", NetworkAddress = "10.0.1.0", Cidr = 24, ParentSubnetId = ParentId }
        ];

        _ = await _controller.BatchCreateChildSubnets(ParentId, subnets, "Azure-VNet-1", isAzureImport: true);

        Subnet? after = await _context.Subnets.FindAsync([ParentId], TestContext.Current.CancellationToken);
        Assert.NotNull(after);
        Assert.Equal("Azure-VNet-1", after.Name);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_FromNonAzureImport_DoesNotRenameParent()
    {

        int parentId = 2;
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

        IActionResult result = await _controller.BatchCreateChildSubnets(parentId, subnets, vnetName);

        _ = Assert.IsType<OkObjectResult>(result);

        Subnet? parentSubnet = await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken);
        Assert.NotNull(parentSubnet);
        Assert.Equal(originalName, parentSubnet.Name);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_OverlappingSubnets_ReturnsValidationError()
    {

        int parentId = 2;

        List<Subnet> existingSubnets = await _context.Subnets
            .Where(s => s.ParentSubnetId == parentId && s.Id != parentId)
            .ToListAsync(TestContext.Current.CancellationToken);
        _context.Subnets.RemoveRange(existingSubnets);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

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
                Cidr = 24,
                ParentSubnetId = parentId
            }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(parentId, subnets);

        BadRequestObjectResult badRequestResult = Assert.IsType<BadRequestObjectResult>(result);

        int subnetCount = await _context.Subnets
            .Where(s => s.ParentSubnetId == parentId && s.Id != parentId)
            .CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, subnetCount);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_EntryContainedInAnEarlierEntry_ReturnsValidationError()
    {
        int parentId = 2;

        List<Subnet> existingSubnets = await _context.Subnets
            .Where(s => s.ParentSubnetId == parentId && s.Id != parentId)
            .ToListAsync(TestContext.Current.CancellationToken);
        _context.Subnets.RemoveRange(existingSubnets);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        _controller.HttpContext.Request.Headers.Referer = "https://localhost/SomeOtherController/Action";

        List<AzureImportSubnetViewModel> subnets =
        [
            new() { Name = "Outer", NetworkAddress = "10.0.1.0", Cidr = 24, ParentSubnetId = parentId },

            new() { Name = "Inner", NetworkAddress = "10.0.1.0", Cidr = 25, ParentSubnetId = parentId }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(parentId, subnets);

        _ = Assert.IsType<BadRequestObjectResult>(result);

        int subnetCount = await _context.Subnets
            .Where(s => s.ParentSubnetId == parentId && s.Id != parentId)
            .CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, subnetCount);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_SubnetsOutsideParent_ReturnsValidationError()
    {

        int parentId = 2;
        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Outside Parent Range",
                NetworkAddress = "192.168.1.0",
                Cidr = 24,
                ParentSubnetId = parentId
            }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(parentId, subnets);

        BadRequestObjectResult badRequestResult = Assert.IsType<BadRequestObjectResult>(result);

        int subnetCount = await _context.Subnets
            .Where(s => s.ParentSubnetId == parentId && s.Id != parentId)
            .CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, subnetCount);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_EmptyList_ReturnsValidationError()
    {

        int parentId = 2;
        string originalName = (await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken))!.Name;

        IActionResult result = await _controller.BatchCreateChildSubnets(
            parentId, [], vnetName: "vnet-production", isAzureImport: true);

        AssertImportFailureRedirect(result, parentId);

        _context.ChangeTracker.Clear();
        Subnet parent = (await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken))!;
        Assert.Equal(originalName, parent.Name);
        Assert.Null(parent.AzureResourceId);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_ParentNotFound_ReturnsNotFound()
    {

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

        IActionResult result = await _controller.BatchCreateChildSubnets(nonExistentParentId, subnets);

        _ = Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_FromAzureImport_ReturnsRedirect()
    {

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

        IActionResult result = await _controller.BatchCreateChildSubnets(parentId, subnets, isAzureImport: true);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(parentId, redirectResult.RouteValues?["id"]);

        Subnet? createdSubnet = await _context.Subnets
            .FirstOrDefaultAsync(s => s.ParentSubnetId == parentId && s.Name == "Azure Import Subnet", TestContext.Current.CancellationToken);

        Assert.NotNull(createdSubnet);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_ImportFailure_RedirectsWithTheReasonInTempData()
    {
        const int parentId = 2;

        IActionResult result = await _controller.BatchCreateChildSubnets(
            parentId, [], vnetName: "vnet-production", isAzureImport: true);

        AssertImportFailureRedirect(result, parentId);
        Assert.Contains("No subnets were submitted", _controller.TempData["ErrorMessage"] as string);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_ApiFailure_StillReturnsBadRequest()
    {
        const int parentId = 2;

        IActionResult result = await _controller.BatchCreateChildSubnets(
            parentId, [], vnetName: null, isAzureImport: false);

        _ = Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(_controller.TempData.ContainsKey("ErrorMessage"));
    }

    [Fact]
    public async Task BatchCreateChildSubnets_AzureImportWithFeatureDisabled_IsRefused()
    {
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "false");

        List<AzureImportSubnetViewModel> subnets =
        [
            new() { Name = "web", NetworkAddress = "10.0.1.0", Cidr = 24, ParentSubnetId = 2 }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(
            2, subnets, vnetName: "prod-vnet",
            vnetResourceId: "/subscriptions/s/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/prod-vnet",
            isAzureImport: true);

        Assert.Empty(await _context.Subnets.Where(s => s.Name == "web").ToListAsync(TestContext.Current.CancellationToken));
        Subnet? parent = await _context.Subnets.FindAsync([2], TestContext.Current.CancellationToken);
        Assert.NotNull(parent);
        Assert.Null(parent.AzureResourceId);
        Assert.Equal("Parent Subnet", parent.Name);
        _ = result;
    }

    [Fact]
    public async Task BatchCreateChildSubnets_ChildAzureIdWithFeatureDisabled_IsRefused()
    {
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "false");

        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {
                Name = "smuggled", NetworkAddress = "10.0.2.0", Cidr = 24, ParentSubnetId = 2,
                AzureResourceId = "/subscriptions/s/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/ghost/subnets/smuggled"
            }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(2, subnets);

        Assert.Empty(await _context.Subnets.Where(s => s.Name == "smuggled").ToListAsync(TestContext.Current.CancellationToken));
        _ = result;
    }

    [Fact]
    public async Task BatchCreateChildSubnets_PlainBatchWithFeatureDisabled_StillCreates()
    {
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "false");

        List<AzureImportSubnetViewModel> subnets =
        [
            new() { Name = "local", NetworkAddress = "10.0.3.0", Cidr = 24, ParentSubnetId = 2 }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(2, subnets);

        Subnet? created = await _context.Subnets.FirstOrDefaultAsync(s => s.Name == "local", TestContext.Current.CancellationToken);
        Assert.NotNull(created);
        Assert.Null(created.AzureResourceId);
        _ = result;
    }

    private const string VNetVa = "/subscriptions/test/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/va";
    private const string VNetVb = "/subscriptions/test/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vb";

    [Fact]
    public async Task BatchCreateChildSubnets_ParentLinkedToADifferentVNet_RefusesAndKeepsTheLink()
    {
        Subnet parent = (await _context.Subnets.FindAsync([2], TestContext.Current.CancellationToken))!;
        parent.AzureResourceId = VNetVa;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        List<AzureImportSubnetViewModel> subnets =
        [
            new() { Name = "web", NetworkAddress = "10.0.1.0", Cidr = 24, ParentSubnetId = 2 }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(
            2, subnets, vnetName: "vb", vnetResourceId: VNetVb, isAzureImport: true);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        string message = Assert.IsType<string>(_controller.TempData["ErrorMessage"]);
        Assert.Contains(VNetVa, message);
        Assert.Contains(VNetVb, message);

        _context.ChangeTracker.Clear();
        Subnet after = (await _context.Subnets.FindAsync([2], TestContext.Current.CancellationToken))!;
        Assert.Equal(VNetVa, after.AzureResourceId);
        Assert.Equal("Parent Subnet", after.Name);
        Assert.False(await _context.Subnets.AnyAsync(s => s.Name == "web", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BatchCreateChildSubnets_ParentLinkedToTheSameVNet_StillImports()
    {

        Subnet parent = (await _context.Subnets.FindAsync([2], TestContext.Current.CancellationToken))!;
        parent.AzureResourceId = VNetVa;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        List<AzureImportSubnetViewModel> subnets =
        [
            new() { Name = "web", NetworkAddress = "10.0.1.0", Cidr = 24, ParentSubnetId = 2 }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(
            2, subnets, vnetName: "va", vnetResourceId: VNetVa, isAzureImport: true);

        _ = Assert.IsType<RedirectToActionResult>(result);
        _context.ChangeTracker.Clear();
        Assert.True(await _context.Subnets.AnyAsync(s => s.Name == "web", TestContext.Current.CancellationToken));
        Subnet after = (await _context.Subnets.FindAsync([2], TestContext.Current.CancellationToken))!;
        Assert.Equal(VNetVa, after.AzureResourceId);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_UnlinkedParent_IsStampedAndCounted()
    {

        List<AzureImportSubnetViewModel> subnets =
        [
            new() { Name = "web", NetworkAddress = "10.0.1.0", Cidr = 24, ParentSubnetId = 2 }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(
            2, subnets, vnetName: "va", vnetResourceId: VNetVa, isAzureImport: true);

        _ = Assert.IsType<RedirectToActionResult>(result);
        _context.ChangeTracker.Clear();
        Subnet after = (await _context.Subnets.FindAsync([2], TestContext.Current.CancellationToken))!;
        Assert.Equal(VNetVa, after.AzureResourceId);
    }
    [Fact]
    public async Task BatchCreateChildSubnets_WhenAnotherRowAlreadyHoldsThisVNet_QualifiesTheParentName()
    {
        const string VNetId = "/subscriptions/s/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/mp";

        _context.Subnets.Add(new Subnet
        {
            Id = 90, Name = "mp", NetworkAddress = "10.101.0.0", Cidr = 16, AzureResourceId = VNetId
        });
        _context.Subnets.Add(new Subnet { Id = 91, Name = "planB", NetworkAddress = "10.102.0.0", Cidr = 16 });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        List<AzureImportSubnetViewModel> subnets =
        [
            new() { Name = "sub-b", NetworkAddress = "10.102.1.0", Cidr = 24, ParentSubnetId = 91 }
        ];

        await _controller.BatchCreateChildSubnets(
            91, subnets, vnetName: "mp", vnetResourceId: VNetId, isAzureImport: true);

        _context.ChangeTracker.Clear();
        Subnet parent = (await _context.Subnets.FindAsync([91], TestContext.Current.CancellationToken))!;
        Assert.Equal("mp (10.102.0.0-16)", parent.Name);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_WhenNoOtherRowHoldsThisVNet_KeepsTheBareName()
    {
        const string VNetId = "/subscriptions/s/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/simple";

        _context.Subnets.Add(new Subnet { Id = 92, Name = "placeholder", NetworkAddress = "10.103.0.0", Cidr = 16 });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        List<AzureImportSubnetViewModel> subnets =
        [
            new() { Name = "s1", NetworkAddress = "10.103.1.0", Cidr = 24, ParentSubnetId = 92 }
        ];

        await _controller.BatchCreateChildSubnets(
            92, subnets, vnetName: "simple", vnetResourceId: VNetId, isAzureImport: true);

        _context.ChangeTracker.Clear();
        Subnet parent = (await _context.Subnets.FindAsync([92], TestContext.Current.CancellationToken))!;
        Assert.Equal("simple", parent.Name);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_RepeatImportIntoTheAlreadyLinkedTarget_DoesNotRename()
    {
        const string VNetId = "/subscriptions/s/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/again";

        _context.Subnets.Add(new Subnet
        {
            Id = 93, Name = "again", NetworkAddress = "10.104.0.0", Cidr = 16, AzureResourceId = VNetId
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        List<AzureImportSubnetViewModel> subnets =
        [
            new() { Name = "s1", NetworkAddress = "10.104.1.0", Cidr = 24, ParentSubnetId = 93 }
        ];

        await _controller.BatchCreateChildSubnets(
            93, subnets, vnetName: "again", vnetResourceId: VNetId, isAzureImport: true);

        _context.ChangeTracker.Clear();
        Subnet parent = (await _context.Subnets.FindAsync([93], TestContext.Current.CancellationToken))!;
        Assert.Equal("again", parent.Name);
    }
}
