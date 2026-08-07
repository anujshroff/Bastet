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
/// The repair path for a subnet whose Azure range moved under a new resource ID. Before it existed
/// such a row could only be archived, which made BASTET advertise a range Azure had already
/// assigned as free space.
///
/// The guard that matters most: the caller supplies no resource ID at all. The server re-scans and
/// re-derives the link, so a stale browser view or a crafted post cannot point a Bastet subnet at an
/// arbitrary Azure resource.
/// </summary>
[Collection(AzureFeatureFlagCollection.Name)]
public class SubnetControllerRelinkAzureSubnetTests : IDisposable
{
    private const string SubId = "11111111-1111-1111-1111-111111111111";
    private const string VNetId =
        $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-a";
    private const string OldSubnetId = $"{VNetId}/subnets/sn-a";
    private const string NewSubnetId = $"{VNetId}/subnets/sn-a2";

    private readonly BastetDbContext _context;
    private readonly SubnetController _controller;
    private readonly IAzureReconciler _reconciler = new AzureReconciler(new IpUtilityService());
    private readonly AzureSubnetSnapshotService _snapshotService;

    public SubnetControllerRelinkAzureSubnetTests()
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

        // A Bastet subnet linked to sn-a, which Azure no longer has.
        _context.Subnets.Add(new Subnet
        {
            Id = 1,
            Name = "app",
            NetworkAddress = "10.111.5.0",
            Cidr = 24,
            AzureResourceId = OldSubnetId,
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

    /// <summary>Azure as it is after the rename: sn-a is gone, sn-a2 carries the same /24.</summary>
    private static MockAzureService AzureAfterRename() =>
        new(true,
            vnets:
            [
                new AzureVNetViewModel { ResourceId = VNetId, Name = "vnet-a", AddressPrefixes = ["10.111.0.0/16"] }
            ],
            subnets:
            [
                new AzureSubnetViewModel { ResourceId = NewSubnetId, Name = "sn-a2", AddressPrefix = "10.111.5.0/24" }
            ]);

    /// <summary>Azure where the range is genuinely gone - nothing holds 10.111.5.0/24 any more.</summary>
    private static MockAzureService AzureAfterGenuineDeletion() =>
        new(true,
            vnets:
            [
                new AzureVNetViewModel { ResourceId = VNetId, Name = "vnet-a", AddressPrefixes = ["10.111.0.0/16"] }
            ],
            subnets:
            [
                new AzureSubnetViewModel { ResourceId = $"{VNetId}/subnets/other", Name = "other", AddressPrefix = "10.111.9.0/24" }
            ]);

    private Task<IActionResult> Relink(int subnetId, IAzureService azureService) =>
        _controller.RelinkAzureSubnet(
            new AzureRelinkDto { SubscriptionId = SubId, SubnetId = subnetId },
            azureService, _reconciler, _snapshotService);

    private async Task<string?> LinkOf(int id) =>
        (await _context.Subnets.FindAsync([id], TestContext.Current.CancellationToken))?.AzureResourceId;

    // -------------------------------------------------------------------------
    // The repair itself
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ARangeThatMovedToANewAzureSubnet_IsRelinkedToIt()
    {
        IActionResult result = await Relink(1, AzureAfterRename());

        _ = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(NewSubnetId, await LinkOf(1));
    }

    [Fact]
    public async Task TheRepairIsIdempotent_ASecondAttemptIsRefusedBecauseThereIsNothingLeftToFix()
    {
        MockAzureService azure = AzureAfterRename();

        _ = Assert.IsType<OkObjectResult>(await Relink(1, azure));
        // Now linked correctly, so the row is no longer reported at all.
        _ = Assert.IsType<ConflictObjectResult>(await Relink(1, azure));
        Assert.Equal(NewSubnetId, await LinkOf(1));
    }

    // -------------------------------------------------------------------------
    // Guards - the endpoint must never write a link Azure does not justify
    // -------------------------------------------------------------------------

    /// <summary>
    /// The counter-test to the repair: a genuinely deleted range has no new owner, so there is
    /// nothing to re-link to and the row must be left exactly as it is - still deletable.
    /// </summary>
    [Fact]
    public async Task ARangeThatIsGenuinelyGone_IsNotRelinked()
    {
        IActionResult result = await Relink(1, AzureAfterGenuineDeletion());

        _ = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(OldSubnetId, await LinkOf(1));
    }

    /// <summary>A subnet that is perfectly healthy is not a repair candidate.</summary>
    [Fact]
    public async Task ASubnetWhoseLinkIsStillLive_IsRefused()
    {
        MockAzureService azure = new(true,
            vnets: [new AzureVNetViewModel { ResourceId = VNetId, Name = "vnet-a", AddressPrefixes = ["10.111.0.0/16"] }],
            subnets: [new AzureSubnetViewModel { ResourceId = OldSubnetId, Name = "sn-a", AddressPrefix = "10.111.5.0/24" }]);

        _ = Assert.IsType<ConflictObjectResult>(await Relink(1, azure));
        Assert.Equal(OldSubnetId, await LinkOf(1));
    }

    /// <summary>
    /// Fail closed. A scan that could not read Azure establishes nothing, so it must not be the
    /// basis for rewriting a link any more than for deleting a row.
    /// </summary>
    [Fact]
    public async Task WhenAzureCannotBeRead_NothingIsChanged()
    {
        IActionResult result = await Relink(1, new MockAzureService(credentialValid: false));

        _ = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(OldSubnetId, await LinkOf(1));
    }

    [Fact]
    public async Task AnUnknownSubnetId_ChangesNothing()
    {
        _ = Assert.IsType<ConflictObjectResult>(await Relink(999, AzureAfterRename()));
        Assert.Equal(OldSubnetId, await LinkOf(1));
    }

    [Fact]
    public async Task WithTheFeatureFlagOff_TheEndpointIsUnavailable()
    {
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "false");

        ObjectResult result = Assert.IsType<ObjectResult>(await Relink(1, AzureAfterRename()));

        Assert.Equal(403, result.StatusCode);
        Assert.Equal(OldSubnetId, await LinkOf(1));
    }

    [Fact]
    public async Task ANullRequest_IsRejected()
    {
        IActionResult result = await _controller.RelinkAzureSubnet(
            null!, AzureAfterRename(), _reconciler, _snapshotService);

        _ = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(OldSubnetId, await LinkOf(1));
    }

    /// <summary>
    /// The point of deriving the link server-side: the range that moved decides what the row links
    /// to, and the client has no say in it. A different subnet in the same VNet holding a different
    /// range is never a candidate, so no request shape can select it.
    /// </summary>
    [Fact]
    public async Task TheNewLinkIsDerivedFromAzure_NotFromAnythingTheCallerSupplied()
    {
        MockAzureService azure = new(true,
            vnets: [new AzureVNetViewModel { ResourceId = VNetId, Name = "vnet-a", AddressPrefixes = ["10.111.0.0/16"] }],
            subnets:
            [
                new AzureSubnetViewModel { ResourceId = NewSubnetId, Name = "sn-a2", AddressPrefix = "10.111.5.0/24" },
                new AzureSubnetViewModel { ResourceId = $"{VNetId}/subnets/decoy", Name = "decoy", AddressPrefix = "10.111.6.0/24" }
            ]);

        _ = Assert.IsType<OkObjectResult>(await Relink(1, azure));

        // The subnet holding THIS row's range, never the other one.
        Assert.Equal(NewSubnetId, await LinkOf(1));
    }

    /// <summary>
    /// O16. RelinkAzureSubnet answers AJAX with no redirectUrl and the wizard never navigates, so
    /// nothing consumed the TempData entry it wrote. ASP.NET Core only removes a TempData entry when
    /// it is READ, so it survived request after request and surfaced later as a green success banner
    /// on an unrelated page - measured landing on a delete-confirmation screen five loads later.
    /// </summary>
    [Fact]
    public async Task ASuccessfulRelink_WritesNoTempDataMessage()
    {
        IActionResult result = await Relink(1, AzureAfterRename());

        _ = Assert.IsType<OkObjectResult>(result);
        Assert.False(_controller.TempData.ContainsKey("SuccessMessage"));
    }
}
