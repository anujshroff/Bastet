using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
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
    private readonly IAzureSubnetSnapshotService _snapshotService;

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
            ControllerTestHelper.CreateMockSubnetLockingService());
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
        IActionResult result = await Delete(Request("approved", 1), new MockAzureService());

        _ = Assert.IsType<OkObjectResult>(result);

        Assert.Null(await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken));
        Assert.Contains(
            await _context.DeletedSubnets.ToListAsync(TestContext.Current.CancellationToken),
            d => d.OriginalId == 1 && d.Name == "vnet-gone");
    }
}
