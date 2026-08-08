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
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.SubnetManagement;

[Collection(Bastet.Tests.Azure.AzureFeatureFlagCollection.Name)]
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
            NetworkAddress = "10.11.0.0",
            Cidr = 24,
            ParentSubnetId = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(parentSubnet);

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
    public async Task BatchCreate_SubnetFullyEncompassesVNetPrefix_MarksParentAsFullyAllocated()
    {

        int parentId = 2;
        string vnetName = "Azure-VNet-1";

        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Default",
                NetworkAddress = "10.11.0.0",
                Cidr = 24,
                Description = "Default subnet",
                Tags = "azure",
                ParentSubnetId = parentId,
                FullyEncompassesVNetPrefix = true
            }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(parentId, subnets, vnetName, isAzureImport: true);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(parentId, redirectResult.RouteValues?["id"]);

        Subnet? parentSubnet = await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken);
        Assert.NotNull(parentSubnet);
        Assert.Equal(vnetName, parentSubnet.Name);
        Assert.True(parentSubnet.IsFullyAllocated);

        Assert.Contains("Default", parentSubnet.Description);
        Assert.Contains("fully allocated", parentSubnet.Description?.ToLower());

        int childSubnetCount = await _context.Subnets
            .Where(s => s.ParentSubnetId == parentId && s.Id != parentId)
            .CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, childSubnetCount);
    }

    [Fact]
    public async Task BatchCreate_FullyEncompassing_NearFullDescription_KeepsItAndStillMarksAllocated()
    {

        int parentId = 2;
        string existingDescription = new('d', 990);
        Subnet parent = (await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken))!;
        parent.Description = existingDescription;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Default",
                NetworkAddress = "10.11.0.0",
                Cidr = 24,
                ParentSubnetId = parentId,
                FullyEncompassesVNetPrefix = true
            }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(
            parentId, subnets, vnetName: "Azure-VNet-5", isAzureImport: true);

        _ = Assert.IsType<RedirectToActionResult>(result);

        _context.ChangeTracker.Clear();
        Subnet updated = (await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken))!;
        Assert.True(updated.IsFullyAllocated);
        Assert.Equal(existingDescription, updated.Description);
    }

    [Fact]
    public async Task BatchCreate_FullyEncompassing_DescriptionWithRoom_GetsTheNoteAppended()
    {

        int parentId = 2;
        Subnet parent = (await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken))!;
        parent.Description = "Original description";
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Default",
                NetworkAddress = "10.11.0.0",
                Cidr = 24,
                ParentSubnetId = parentId,
                FullyEncompassesVNetPrefix = true
            }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(
            parentId, subnets, vnetName: "Azure-VNet-6", isAzureImport: true);

        _ = Assert.IsType<RedirectToActionResult>(result);

        _context.ChangeTracker.Clear();
        Subnet updated = (await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken))!;
        Assert.StartsWith("Original description", updated.Description);
        Assert.Contains("Fully allocated by Azure subnet 'Default'", updated.Description);
        Assert.True(updated.Description!.Length <= 1000);
    }

    [Fact]
    public async Task BatchCreate_FullyEncompassing_ParentHasChildren_IsRejectedAndParentUntouched()
    {

        int parentId = 2;
        _context.Subnets.Add(new Subnet
        {
            Id = 3,
            Name = "Existing child",
            NetworkAddress = "10.11.0.0",
            Cidr = 25,
            ParentSubnetId = parentId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Default",
                NetworkAddress = "10.11.0.0",
                Cidr = 24,
                ParentSubnetId = parentId,
                FullyEncompassesVNetPrefix = true
            }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(
            parentId, subnets, vnetName: "Azure-VNet-3", isAzureImport: true);

        AssertImportFailureRedirect(result, parentId);

        _context.ChangeTracker.Clear();
        Subnet parent = (await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken))!;
        Assert.False(parent.IsFullyAllocated);
        Assert.Equal("Parent Subnet", parent.Name);
    }

    [Fact]
    public async Task BatchCreate_FullyEncompassing_PrefixDoesNotCoverParent_IsRejectedAndParentUntouched()
    {

        int parentId = 2;
        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Default",
                NetworkAddress = "192.168.99.0",
                Cidr = 24,
                ParentSubnetId = parentId,
                FullyEncompassesVNetPrefix = true
            }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(
            parentId, subnets, vnetName: "Azure-VNet-4", isAzureImport: true);

        AssertImportFailureRedirect(result, parentId);

        _context.ChangeTracker.Clear();
        Subnet parent = (await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken))!;
        Assert.False(parent.IsFullyAllocated);
        Assert.Equal("Parent Subnet", parent.Name);
    }

    [Fact]
    public async Task BatchCreate_EncompassingEntryWithSiblings_IsRefusedAndWritesNothing()
    {

        int parentId = 2;
        string vnetName = "Azure-VNet-2";

        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Default",
                NetworkAddress = "10.11.0.0",
                Cidr = 24,
                Description = "Default subnet",
                Tags = "azure",
                ParentSubnetId = parentId,
                FullyEncompassesVNetPrefix = true
            },
            new()
            {
                Name = "Subnet1",
                NetworkAddress = "10.11.0.0",
                Cidr = 25,
                Description = "Regular subnet 1",
                Tags = "azure",
                ParentSubnetId = parentId,
                FullyEncompassesVNetPrefix = false
            },
            new()
            {
                Name = "Subnet2",
                NetworkAddress = "10.11.0.128",
                Cidr = 25,
                Description = "Regular subnet 2",
                Tags = "azure",
                ParentSubnetId = parentId,
                FullyEncompassesVNetPrefix = false
            }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(parentId, subnets, vnetName, isAzureImport: true);

        AssertImportFailureRedirect(result, parentId);

        _context.ChangeTracker.Clear();

        Subnet? parentSubnet = await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken);
        Assert.NotNull(parentSubnet);
        Assert.NotEqual(vnetName, parentSubnet.Name);
        Assert.False(parentSubnet.IsFullyAllocated);

        int childSubnetCount = await _context.Subnets
            .Where(s => s.ParentSubnetId == parentId && s.Id != parentId)
            .CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, childSubnetCount);
    }

    private static List<AzureImportSubnetViewModel> EncompassingEntry(int parentId) =>
    [
        new()
        {
            Name = "Default",
            NetworkAddress = "10.11.0.0",
            Cidr = 24,
            ParentSubnetId = parentId,
            FullyEncompassesVNetPrefix = true
        }
    ];

    [Fact]
    public async Task BatchCreate_FullyEncompassing_WithoutAzureImportFlag_IsRejected()
    {
        const int parentId = 2;
        string? originalName = (await _context.Subnets
            .FindAsync([parentId], TestContext.Current.CancellationToken))?.Name;

        IActionResult result = await _controller.BatchCreateChildSubnets(
            parentId, EncompassingEntry(parentId), vnetName: "Azure-VNet-1", isAzureImport: false);

        _ = Assert.IsType<BadRequestObjectResult>(result);

        Subnet? parent = await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken);
        Assert.NotNull(parent);
        Assert.Equal(originalName, parent.Name);
        Assert.False(parent.IsFullyAllocated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task BatchCreate_FullyEncompassing_WithoutVNetName_IsRejected(string? vnetName)
    {
        const int parentId = 2;

        IActionResult result = await _controller.BatchCreateChildSubnets(
            parentId, EncompassingEntry(parentId), vnetName: vnetName, isAzureImport: true);

        AssertImportFailureRedirect(result, parentId);

        Subnet? parent = await _context.Subnets.FindAsync([parentId], TestContext.Current.CancellationToken);
        Assert.NotNull(parent);
        Assert.False(parent.IsFullyAllocated);
    }

    [Fact]
    public async Task BatchCreate_OrdinaryChildren_WithoutAzureImportFlag_StillWorks()
    {
        const int parentId = 2;
        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {
                Name = "Child",
                NetworkAddress = "10.11.0.0",
                Cidr = 25,
                ParentSubnetId = parentId,
                FullyEncompassesVNetPrefix = false
            }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(
            parentId, subnets, vnetName: null, isAzureImport: false);

        _ = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, await _context.Subnets
            .CountAsync(s => s.ParentSubnetId == parentId, TestContext.Current.CancellationToken));
    }
}
