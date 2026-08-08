using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Bastet.Tests.Azure;

[Collection(AzureFeatureFlagCollection.Name)]
public class SubnetControllerAzureReconcileTests : IDisposable
{
    private const string SubId = "11111111-1111-1111-1111-111111111111";

    private readonly BastetDbContext _context;
    private readonly SubnetController _controller;
    private readonly IAzureReconciler _reconciler = new AzureReconciler(new IpUtilityService());
    private readonly AzureSubnetSnapshotService _snapshotService;

    public SubnetControllerAzureReconcileTests()
    {
        DbContextOptions<BastetDbContext> options = new DbContextOptionsBuilder<BastetDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new BastetDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        IUserContextService userContextService = ControllerTestHelper.CreateMockUserContextService();
        IIpUtilityService ipUtilityService = new IpUtilityService();
        _snapshotService = new AzureSubnetSnapshotService(_context);

        _controller = new SubnetController(
            _context,
            ipUtilityService,
            new SubnetValidationService(ipUtilityService),
            new HostIpValidationService(ipUtilityService, _context),
            userContextService,
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<SubnetController>.Instance);
        ControllerTestHelper.SetupController(_controller);

        _controller.Url = Mock.Of<IUrlHelper>();

        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "true");

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
        _context.Subnets.Add(new Subnet
        {
            Id = 1,
            Name = "vnet-gone",
            NetworkAddress = "10.0.0.0",
            Cidr = 16,
            AzureResourceId = $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-gone",
            CreatedAt = DateTime.UtcNow
        });
        _context.SaveChanges();
    }

    private Task<IActionResult> Delete(AzureReconcileDeleteDto request, IAzureService azureService) =>
        _controller.BulkDeleteStaleAzureSubnets(request, azureService, _reconciler, _snapshotService);

    private static AzureReconcileDeleteDto Request(string confirmation, params int[] subnetIds) =>
        new() { SubscriptionId = SubId, SubnetIds = [.. subnetIds], Confirmation = confirmation };

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_WrongConfirmation_ReturnsBadRequest()
    {
        IActionResult result = await Delete(Request("yes", 1), new MockAzureService());

        BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("approved", bad.Value?.ToString());

        Assert.NotNull(await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_EmptyConfirmation_ReturnsBadRequest()
    {
        IActionResult result = await Delete(Request(string.Empty, 1), new MockAzureService());

        _ = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_CorrectConfirmationButNoSubnetIds_ReturnsBadRequest()
    {

        IActionResult result = await Delete(Request("approved"), new MockAzureService());

        _ = Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_ScanFails_DeletesNothing()
    {

        IActionResult result = await Delete(Request("approved", 1), new MockAzureService(credentialValid: false));

        _ = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_SubnetNoLongerStale_ReturnsConflictAndDeletesNothing()
    {

        List<AzureVNetViewModel> vnets =
        [
            new()
            {
                ResourceId = $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-gone",
                Name = "vnet-gone",
                AddressPrefixes = ["10.0.0.0/16"]
            }
        ];

        IActionResult result = await Delete(Request("approved", 1), new MockAzureService(true, null, vnets));

        _ = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken));
    }

    private const string VNetGoneId =
        $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-gone";

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_ResourceNotVisibleRatherThanDeleted_DeletesNothing()
    {
        MockAzureService azure = new();
        azure.Confirmations[VNetGoneId] = AzureResourceConfirmation.NotVisible;

        IActionResult result = await Delete(Request("approved", 1), azure);

        _ = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_ConfirmationUnknown_DeletesNothing()
    {
        MockAzureService azure = new();
        azure.Confirmations[VNetGoneId] = AzureResourceConfirmation.Unknown;

        IActionResult result = await Delete(Request("approved", 1), azure);

        _ = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_ResourceStillLive_DeletesNothing()
    {
        MockAzureService azure = new();
        azure.Confirmations[VNetGoneId] = AzureResourceConfirmation.Live;

        IActionResult result = await Delete(Request("approved", 1), azure);

        _ = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_FeatureFlagOff_Returns403()
    {
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "false");

        IActionResult result = await Delete(Request("approved", 1), new MockAzureService());

        ObjectResult objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, objectResult.StatusCode);
        Assert.NotNull(await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_StaleSubnetCorrectlyConfirmed_DeletesAndArchives()
    {

        MockAzureService azure = new();
        AzureReconcileDeleteDto request = Request("approved", 1);

        request.Statuses = await AzureReconcileApproval.ForAsync(azure, _snapshotService, SubId, [1]);

        IActionResult result = await Delete(request, azure);

        _ = Assert.IsType<OkObjectResult>(result);

        Assert.Null(await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken));
        Assert.Contains(
            await _context.DeletedSubnets.ToListAsync(TestContext.Current.CancellationToken),
            d => d.OriginalId == 1 && d.Name == "vnet-gone");
    }

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_DriftOnlyPlanOverReviewItemDescendant_IsRefused()
    {

        _context.Subnets.RemoveRange(_context.Subnets);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        const string HubId = $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/hub";
        const string FaId = $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/fa";

        _context.Subnets.AddRange(
            new Subnet
            {
                Id = 10,
                Name = "hub",
                NetworkAddress = "10.96.0.0",
                Cidr = 15,
                AzureResourceId = HubId,
                CreatedAt = DateTime.UtcNow
            },
            new Subnet
            {
                Id = 11,
                Name = "fa",
                NetworkAddress = "10.97.0.0",
                Cidr = 16,
                ParentSubnetId = 10,
                IsFullyAllocated = true,
                AzureResourceId = FaId,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        List<AzureVNetViewModel> vnets =
        [
            new() { ResourceId = HubId, Name = "hub", AddressPrefixes = ["10.100.0.0/15"] },
            new() { ResourceId = FaId, Name = "fa", AddressPrefixes = ["10.97.0.0/16"] }
        ];

        IActionResult result = await Delete(Request("approved", 10), new MockAzureService(true, null, vnets));

        _ = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(await _context.Subnets.FindAsync([10], TestContext.Current.CancellationToken));
        Assert.NotNull(await _context.Subnets.FindAsync([11], TestContext.Current.CancellationToken));
        Assert.Empty(await _context.DeletedSubnets.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAzureLinkedSubnets_DescendantSubnetIds_CoverTheWholeSubtree()
    {

        _context.Subnets.AddRange(
            new Subnet
            {
                Id = 2,
                Name = "child",
                NetworkAddress = "10.0.1.0",
                Cidr = 24,
                ParentSubnetId = 1,
                AzureResourceId = $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-gone/subnets/child",
                CreatedAt = DateTime.UtcNow
            },
            new Subnet
            {
                Id = 3,
                Name = "grandchild",
                NetworkAddress = "10.0.1.0",
                Cidr = 25,
                ParentSubnetId = 2,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<AzureLinkedSubnetSnapshot> snapshots = await _snapshotService.GetAzureLinkedSubnetsAsync();

        AzureLinkedSubnetSnapshot root = Assert.Single(snapshots, s => s.Id == 1);
        Assert.Equal([2, 3], root.DescendantSubnetIds.Order());
        Assert.Equal(root.DescendantCount, root.DescendantSubnetIds.Count);

        AzureLinkedSubnetSnapshot child = Assert.Single(snapshots, s => s.Id == 2);
        Assert.Equal([3], child.DescendantSubnetIds);
    }
}
