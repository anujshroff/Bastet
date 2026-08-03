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

/// <summary>
/// Tests for the Azure Reconcile commit endpoint. This endpoint bulk-deletes subnets, so its guards
/// are the point: the typed confirmation, and refusing to act when Azure could not be re-checked.
/// </summary>
/// <remarks>
/// The action is invoked directly, so MVC filters (authorization, antiforgery) don't run - which is
/// deliberate. Those are covered by ControllerAuthorizationTests; these tests isolate the guards
/// inside the method body, which a manual HTTP call cannot reach without a session cookie and an
/// antiforgery token.
/// </remarks>
[Collection(AzureFeatureFlagCollection.Name)]
public class SubnetControllerAzureReconcileTests : IDisposable
{
    private const string SubId = "11111111-1111-1111-1111-111111111111";

    private readonly BastetDbContext _context;
    private readonly SubnetController _controller;
    private readonly IAzureReconciler _reconciler = new AzureReconciler();
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

        // The action calls Url.Action after committing; without RequestServices the Url helper is
        // null, so supply one rather than letting an NRE surface as a bogus 500.
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

    /// <summary>
    /// One subnet imported from a VNet that no longer exists in Azure, so a successful scan reports
    /// it as stale and it is genuinely deletable.
    /// </summary>
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

    // -------------------------------------------------------------------------
    // The typed confirmation gate
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_WrongConfirmation_ReturnsBadRequest()
    {
        IActionResult result = await Delete(Request("yes", 1), new MockAzureService());

        BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("approved", bad.Value?.ToString());

        // Nothing was touched
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
        // "approved" on its own is not a licence to do anything
        IActionResult result = await Delete(Request("approved"), new MockAzureService());

        _ = Assert.IsType<BadRequestObjectResult>(result);
    }

    // -------------------------------------------------------------------------
    // Fail closed at the commit endpoint, not just the scan
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_ScanFails_DeletesNothing()
    {
        // credentialValid: false makes GetVNetInventory report failure. The re-scan then reports no
        // stale subnets, so even a correctly-confirmed request for a genuinely stale subnet must be
        // refused - "Azure didn't answer" is not "the resource is gone".
        IActionResult result = await Delete(Request("approved", 1), new MockAzureService(credentialValid: false));

        _ = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_SubnetNoLongerStale_ReturnsConflictAndDeletesNothing()
    {
        // The scan succeeds and finds the VNet alive, so the subnet the client asked to delete is no
        // longer stale. Guards against committing a selection built from an out-of-date scan.
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

    // -------------------------------------------------------------------------
    // Absence from an RBAC-filtered listing is not a deletion
    // -------------------------------------------------------------------------

    private const string VNetGoneId =
        $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-gone";

    /// <summary>
    /// ARM list operations are RBAC-filtered: a credential that loses access to a resource group
    /// gets HTTP 200 with those resources simply absent, which the scan cannot tell apart from
    /// deletion. A direct read can (403 versus 404), so a subnet Azure will not confirm is gone
    /// must never be archived - however "stale" the listing made it look.
    /// </summary>
    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_ResourceNotVisibleRatherThanDeleted_DeletesNothing()
    {
        MockAzureService azure = new();                            // empty inventory => looks stale
        azure.Confirmations[VNetGoneId] = AzureResourceConfirmation.NotVisible;

        IActionResult result = await Delete(Request("approved", 1), azure);

        _ = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken));
    }

    /// <summary>An unanswered question is not a deletion either.</summary>
    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_ConfirmationUnknown_DeletesNothing()
    {
        MockAzureService azure = new();
        azure.Confirmations[VNetGoneId] = AzureResourceConfirmation.Unknown;

        IActionResult result = await Delete(Request("approved", 1), azure);

        _ = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The listing said gone, the direct read says it is still there. Trust the direct read.
    /// </summary>
    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_ResourceStillLive_DeletesNothing()
    {
        MockAzureService azure = new();
        azure.Confirmations[VNetGoneId] = AzureResourceConfirmation.Live;

        IActionResult result = await Delete(Request("approved", 1), azure);

        _ = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken));
    }

    // -------------------------------------------------------------------------
    // Feature flag
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_FeatureFlagOff_Returns403()
    {
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "false");

        IActionResult result = await Delete(Request("approved", 1), new MockAzureService());

        ObjectResult objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, objectResult.StatusCode);
        Assert.NotNull(await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken));
    }

    // -------------------------------------------------------------------------
    // The happy path, so the guards above aren't passing vacuously
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_StaleSubnetCorrectlyConfirmed_DeletesAndArchives()
    {
        // An empty-but-successful inventory means the VNet really is gone.
        MockAzureService azure = new();
        AzureReconcileDeleteDto request = Request("approved", 1);
        // Approve exactly what a scan reports, as the wizard does - the verdict is now part of the
        // request and a batch that names none is refused.
        request.Statuses = await AzureReconcileApproval.ForAsync(azure, _snapshotService, SubId, [1]);

        IActionResult result = await Delete(request, azure);

        _ = Assert.IsType<OkObjectResult>(result);

        Assert.Null(await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken));
        Assert.Contains(
            await _context.DeletedSubnets.ToListAsync(TestContext.Current.CancellationToken),
            d => d.OriginalId == 1 && d.Name == "vnet-gone");
    }

    // -------------------------------------------------------------------------
    // The cascade guard must not depend on some other row being absent
    // -------------------------------------------------------------------------

    /// <summary>
    /// A plan built entirely from prefix drift carries no absence claim, so there is nothing to ask
    /// Azure about - but the cascade guard must still run. Here the drifted target's subtree holds a
    /// review item, a row this very scan verified is still live in Azure, and archiving the target
    /// would archive it too.
    /// </summary>
    /// <remarks>
    /// Regression for the round-9 I1 defect: ConfirmProposedDeletionsAsync returned early when
    /// absenceClaims was empty, which skipped ApplyConfirmations and with it the guard over
    /// plan.ReviewItems. Whether the archive was refused then depended on some unrelated row
    /// happening to be absent, so adding a stale subnet elsewhere in the tree changed the verdict
    /// for these two rows. The guard must be reached on the strength of this subtree alone.
    /// </remarks>
    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_DriftOnlyPlanOverReviewItemDescendant_IsRefused()
    {
        // The seeded row is a VNetDeleted (absence) row; it would supply the absence claim whose
        // absence is the whole point of this test.
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

        // hub drifted (Azure now carries 10.100.0.0/15, not 10.96.0.0/15) => VNetPrefixRemoved,
        // which is drift, not absence. fa still carries its recorded prefix but no Azure subnet
        // covers it any more => FullyAllocatingSubnetDeleted, a review item.
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

    // -------------------------------------------------------------------------
    // Snapshot subtree ids - what the confirm dialog's dedup rests on
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAzureLinkedSubnets_DescendantSubnetIds_CoverTheWholeSubtree()
    {
        // The ids must match what DescendantCount counts, or the client-side dedup of
        // overlapping selections drifts from the numbers it corrects.
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

        // The Azure-linked child's own subtree is just the grandchild.
        AzureLinkedSubnetSnapshot child = Assert.Single(snapshots, s => s.Id == 2);
        Assert.Equal([3], child.DescendantSubnetIds);
    }
}
