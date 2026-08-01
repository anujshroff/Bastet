using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Bastet.Tests.Azure;

/// <summary>
/// The bulk Azure import commit re-derives its plan against the tree as it is at commit time. That
/// stops a stale preview writing stale decisions, but it also means the plan actually executed can
/// differ from the one the operator read and approved.
/// </summary>
/// <remarks>
/// Regression for round-10 J2. The commit posts the selection, never the plan, so the preview and
/// commit request bodies were byte-identical on the wire and nothing compared the two plans. A
/// subnet created by a second admin while the preview was on screen flipped the target from
/// AutoCreateTopLevel to ExactMatch, and the commit adopted that subnet: stamped it with the VNet's
/// <c>AzureResourceId</c>, renamed it if the rename box was ticked, and pulled it into the reconcile
/// wizard's deletion scope. None of that is reversible in the application - no view clears
/// <c>AzureResourceId</c>, and the archive table does not carry it.
/// </remarks>
[Collection(AzureFeatureFlagCollection.Name)]
public class SubnetControllerBulkAzureImportTests : IDisposable
{
    private const string SubId = "22222222-2222-2222-2222-222222222222";
    private const string VNetId =
        $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/rig-div";

    private readonly BastetDbContext _context;
    private readonly SubnetController _controller;
    private readonly IAzureBulkImportPlanner _planner;
    private readonly IAzureSubnetSnapshotService _snapshotService;

    public SubnetControllerBulkAzureImportTests()
    {
        DbContextOptions<BastetDbContext> options = new DbContextOptionsBuilder<BastetDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new BastetDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        IIpUtilityService ipUtilityService = new IpUtilityService();
        IInputSanitizationService sanitizationService = new InputSanitizationService();
        _planner = new AzureBulkImportPlanner(ipUtilityService, sanitizationService);
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
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", null);
        _context.Database.CloseConnection();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private Task<IActionResult> Commit(BulkImportSelectionDto selection) =>
        _controller.BulkCreateFromAzurePlan(selection, _planner, _snapshotService, null);

    /// <summary>
    /// The selection the browser posts for one VNet prefix, carrying what the preview approved.
    /// </summary>
    private static BulkImportSelectionDto Selection(BulkImportExpectedTargetDto? expected, bool rename = false) =>
        new()
        {
            SubscriptionId = SubId,
            VNetPrefixes =
            [
                new BulkImportSelectedVNetPrefixDto
                {
                    VNetName = "rig-div",
                    VNetResourceId = VNetId,
                    AddressPrefix = "10.151.0.0/16",
                    Subnets = [],
                    Expected = expected
                }
            ],
            RenameMatchedBastetSubnets = rename
        };

    /// <summary>
    /// What the preview showed when nothing in Bastet covered the VNet prefix: a brand-new top-level
    /// subnet would be created for it.
    /// </summary>
    private static BulkImportExpectedTargetDto ApprovedNewTopLevel() =>
        new()
        {
            TargetType = nameof(BulkImportTargetType.AutoCreateTopLevel),
            ExistingTargetSubnetId = null,
            AutoCreateParentSubnetId = null,
            WillRename = false,
            NewName = null,
            WillMarkFullyAllocated = false
        };

    /// <summary>
    /// The second admin: an ordinary hand-made subnet on the same prefix, with no Azure link.
    /// </summary>
    private async Task InterleaveHandCreatedSubnetAsync() =>
        await AddSubnetAsync("Finance-Prod-Reserved", "10.151.0.0", 16);

    private async Task AddSubnetAsync(string name, string network, int cidr)
    {
        _context.Subnets.Add(new Subnet
        {
            Name = name,
            NetworkAddress = network,
            Cidr = cidr,
            Description = "Reserved by the network team. Not an Azure VNet.",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task BulkCreateFromAzurePlan_TargetAdoptedAfterPreview_IsRefusedAndWritesNothing()
    {
        // Approved when the tree was empty: "create a new top-level subnet".
        BulkImportSelectionDto selection = Selection(ApprovedNewTopLevel());

        // ... and then someone else creates that very prefix by hand.
        await InterleaveHandCreatedSubnetAsync();

        IActionResult result = await Commit(selection);

        _ = Assert.IsType<ConflictObjectResult>(result);

        // The hand-made subnet is untouched: not linked to Azure, not renamed.
        Subnet reserved = await _context.Subnets.SingleAsync(
            s => s.Name == "Finance-Prod-Reserved", TestContext.Current.CancellationToken);
        Assert.Null(reserved.AzureResourceId);

        // And nothing new was created for the VNet.
        Assert.Equal(1, await _context.Subnets.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BulkCreateFromAzurePlan_TargetAdoptedAfterPreview_ConflictNamesTheDivergence()
    {
        BulkImportSelectionDto selection = Selection(ApprovedNewTopLevel());
        await InterleaveHandCreatedSubnetAsync();

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(await Commit(selection));

        // The response has to say what changed, or the operator cannot tell this from a transient
        // failure and will simply retry into the same adoption. Serialized rather than inspected
        // field by field, so this also asserts on what actually reaches the browser.
        string body = System.Text.Json.JsonSerializer.Serialize(conflict.Value);
        Assert.Contains("10.151.0.0/16", body, StringComparison.Ordinal);
        Assert.Contains(nameof(BulkImportTargetType.ExactMatch), body, StringComparison.Ordinal);

        // The caller's own strings are never echoed back - the nested selection list is not visited
        // by GlobalSanitizationFilter, so nothing on the expectation has been sanitized.
        Assert.DoesNotContain("Reserved by the network team", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BulkCreateFromAzurePlan_PlanUnchangedSincePreview_StillCommits()
    {
        // The approved outcome and the re-derived one agree, which is the ordinary case; the guard
        // must not stand in its way.
        BulkImportSelectionDto selection = Selection(ApprovedNewTopLevel());

        IActionResult result = await Commit(selection);

        _ = Assert.IsType<OkObjectResult>(result);

        Subnet created = await _context.Subnets.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("10.151.0.0", created.NetworkAddress);
        Assert.Equal(VNetId, created.AzureResourceId);
    }

    [Fact]
    public async Task BulkCreateFromAzurePlan_AdoptingASubnetThatWasApprovedForAdoption_StillCommits()
    {
        // The advertised adopt path: the operator previewed an ExactMatch onto an unlinked subnet
        // and approved it. Refusing this would break the feature, so it must still go through.
        await InterleaveHandCreatedSubnetAsync();
        int existingId = (await _context.Subnets.SingleAsync(TestContext.Current.CancellationToken)).Id;

        BulkImportSelectionDto selection = Selection(new BulkImportExpectedTargetDto
        {
            TargetType = nameof(BulkImportTargetType.ExactMatch),
            ExistingTargetSubnetId = existingId,
            AutoCreateParentSubnetId = null,
            WillRename = false,
            NewName = null,
            WillMarkFullyAllocated = false
        });

        IActionResult result = await Commit(selection);

        _ = Assert.IsType<OkObjectResult>(result);

        Subnet adopted = await _context.Subnets.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(VNetId, adopted.AzureResourceId);
    }

    [Fact]
    public async Task BulkCreateFromAzurePlan_NoApprovedOutcome_IsNotRefused()
    {
        // A direct JSON caller that never previewed has approved nothing, so there is nothing to
        // compare against. Documented behaviour is preserved rather than broken; the commit records
        // the unverified prefix in the log instead.
        await InterleaveHandCreatedSubnetAsync();

        IActionResult result = await Commit(Selection(expected: null));

        _ = Assert.IsType<OkObjectResult>(result);
    }
}
