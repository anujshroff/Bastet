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
/// The reconcile commit re-derives Azure verdicts and archives whatever is still stale. Testing set
/// membership is not the same as testing consent: a row approved under "the Azure resource no longer
/// exists" can still be in the plan moments later under "the prefix changed" - a different fact,
/// reached with no direct ARM read - and the subtree was archived on an approval whose stated
/// premise the server had itself disproved.
///
/// The endpoint's own docstring promises "a resource that reappeared in Azure cannot cause the wrong
/// subnets to be archived". These tests are that promise.
/// </summary>
[Collection(AzureFeatureFlagCollection.Name)]
public class SubnetControllerReconcileApprovedVerdictTests : IDisposable
{
    private const string SubId = "11111111-1111-1111-1111-111111111111";
    private const string VNetId =
        $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-a";

    private readonly BastetDbContext _context;
    private readonly SubnetController _controller;
    private readonly IAzureReconciler _reconciler = new AzureReconciler();
    private readonly AzureSubnetSnapshotService _snapshotService;

    public SubnetControllerReconcileApprovedVerdictTests()
    {
        DbContextOptions<BastetDbContext> options = new DbContextOptionsBuilder<BastetDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new BastetDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        IIpUtilityService ipUtilityService = new IpUtilityService();
        _snapshotService = new AzureSubnetSnapshotService(_context);

        _controller = new SubnetController(
            _context,
            ipUtilityService,
            new SubnetValidationService(ipUtilityService),
            new HostIpValidationService(ipUtilityService, _context),
            ControllerTestHelper.CreateMockUserContextService(),
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<SubnetController>.Instance);
        ControllerTestHelper.SetupController(_controller);
        _controller.Url = Mock.Of<IUrlHelper>();

        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "true");

        // The import target, plus a hand-created child and a host IP carrying no Azure provenance
        // at all - the rows an archive on a disproved premise takes with it.
        _context.Subnets.Add(new Subnet
        {
            Id = 1,
            Name = "vnet-a",
            NetworkAddress = "10.111.0.0",
            Cidr = 16,
            AzureResourceId = VNetId,
            CreatedAt = DateTime.UtcNow
        });
        _context.Subnets.Add(new Subnet
        {
            Id = 2,
            Name = "prod-app-tier",
            NetworkAddress = "10.111.1.0",
            Cidr = 24,
            ParentSubnetId = 1,
            CreatedAt = DateTime.UtcNow
        });
        _context.SaveChanges();
        _context.HostIpAssignments.Add(new HostIpAssignment
        {
            SubnetId = 2,
            IP = "10.111.1.10",
            Name = "web01",
            CreatedAt = DateTime.UtcNow
        });
        _context.SaveChanges();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", null);
        _context.Database.CloseConnection();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The VNet is back at the same ARM id (ids are path-based, so an IaC destroy/apply recreates
    /// the same id) but with a different prefix. The row is still stale - as VNetPrefixRemoved, a
    /// drift verdict taken off the listing with no direct ARM read - not as VNetDeleted.
    /// </summary>
    private static MockAzureService VNetBackWithADifferentPrefix() =>
        new(true,
            vnets: [new AzureVNetViewModel { ResourceId = VNetId, Name = "vnet-a", AddressPrefixes = ["10.112.0.0/16"] }]);

    private Task<IActionResult> Delete(AzureReconcileDeleteDto request, IAzureService azure) =>
        _controller.BulkDeleteStaleAzureSubnets(request, azure, _reconciler, _snapshotService);

    private static AzureReconcileDeleteDto Request(params AzureReconcileApprovedVerdict[] verdicts) =>
        new()
        {
            SubscriptionId = SubId,
            SubnetIds = [1],
            Confirmation = "approved",
            Statuses = [.. verdicts]
        };

    private async Task AssertNothingArchived()
    {
        Assert.NotNull(await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken));
        Assert.NotNull(await _context.Subnets.FindAsync([2], TestContext.Current.CancellationToken));
        Assert.Equal(1, await _context.HostIpAssignments.CountAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await _context.DeletedSubnets.ToListAsync(TestContext.Current.CancellationToken));
    }

    // -------------------------------------------------------------------------
    // The defect
    // -------------------------------------------------------------------------

    /// <summary>
    /// Approved as "the VNet no longer exists"; by commit time the server itself says it does exist
    /// and merely changed prefix. The id is still in the plan, so the existing membership check
    /// passes it straight through.
    /// </summary>
    [Fact]
    public async Task ApprovedAsDeleted_ButReDerivedAsDrift_IsRefusedAndArchivesNothing()
    {
        IActionResult result = await Delete(
            Request(new AzureReconcileApprovedVerdict
            {
                SubnetId = 1,
                StatusName = nameof(AzureReconcileStatus.VNetDeleted),
                Reason = "The VNet this subnet was imported from no longer exists in Azure, or no longer has any IPv4 address space."
            }),
            VNetBackWithADifferentPrefix());

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("has changed since", conflict.Value?.ToString());
        await AssertNothingArchived();
    }

    /// <summary>
    /// Same status, different facts. Comparing the status alone would let this through, which is why
    /// the reason is compared too.
    /// </summary>
    [Fact]
    public async Task ApprovedWithTheSameStatusButADifferentReason_IsRefused()
    {
        IActionResult result = await Delete(
            Request(new AzureReconcileApprovedVerdict
            {
                SubnetId = 1,
                StatusName = nameof(AzureReconcileStatus.VNetPrefixRemoved),
                Reason = "VNet 'vnet-a' still exists but no longer has the address prefix 10.99.0.0/16."
            }),
            VNetBackWithADifferentPrefix());

        _ = Assert.IsType<ConflictObjectResult>(result);
        await AssertNothingArchived();
    }

    // -------------------------------------------------------------------------
    // Mandatory: an omitted or unusable verdict is not consent
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ARequestNamingNoVerdictAtAll_IsRefused()
    {
        _ = Assert.IsType<ConflictObjectResult>(await Delete(Request(), VNetBackWithADifferentPrefix()));
        await AssertNothingArchived();
    }

    [Fact]
    public async Task AVerdictForADifferentRow_DoesNotApproveThisOne()
    {
        IActionResult result = await Delete(
            Request(new AzureReconcileApprovedVerdict
            {
                SubnetId = 99,
                StatusName = nameof(AzureReconcileStatus.VNetPrefixRemoved),
                Reason = "VNet 'vnet-a' still exists but no longer has the address prefix 10.111.0.0/16."
            }),
            VNetBackWithADifferentPrefix());

        _ = Assert.IsType<ConflictObjectResult>(result);
        await AssertNothingArchived();
    }

    /// <summary>
    /// A status name that parses to nothing establishes nothing, so it is a divergence rather than
    /// "unverified" - the same rule the bulk import commit applies to an unparseable TargetType.
    /// </summary>
    [Theory]
    [InlineData("NotAStatus")]
    [InlineData("")]
    [InlineData("42")]
    public async Task AnUnparseableStatusName_IsTreatedAsADivergence(string statusName)
    {
        IActionResult result = await Delete(
            Request(new AzureReconcileApprovedVerdict
            {
                SubnetId = 1,
                StatusName = statusName,
                Reason = "VNet 'vnet-a' still exists but no longer has the address prefix 10.111.0.0/16."
            }),
            VNetBackWithADifferentPrefix());

        _ = Assert.IsType<ConflictObjectResult>(result);
        await AssertNothingArchived();
    }

    // -------------------------------------------------------------------------
    // Counter-test - a matching verdict must still archive, or the feature is dead
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AVerdictThatStillMatches_ArchivesNormally()
    {
        MockAzureService azure = VNetBackWithADifferentPrefix();

        AzureReconcileDeleteDto request = Request();
        request.Statuses = await AzureReconcileApproval.ForAsync(azure, _snapshotService, SubId, [1]);

        // Sanity: the scan really did produce a verdict to approve.
        Assert.Single(request.Statuses);
        Assert.Equal(nameof(AzureReconcileStatus.VNetPrefixRemoved), request.Statuses[0].StatusName);

        IActionResult result = await Delete(request, azure);

        _ = Assert.IsType<OkObjectResult>(result);
        Assert.Null(await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken));
        Assert.Null(await _context.Subnets.FindAsync([2], TestContext.Current.CancellationToken));
    }
}
