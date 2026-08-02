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

    /// <summary>
    /// A concurrent rename that moves the planned child names must be refused, not committed.
    /// </summary>
    /// <remarks>
    /// Round-11 K4. The approved-plan expectation carried the target's identity but nothing about
    /// the children the commit actually writes. <c>BuildPlanItem</c> seeds its disambiguation set
    /// from the existing tree, so renaming the matched Bastet subnet moves <c>DisambiguateName</c>'s
    /// output while every compared field stays equal - the commit returned 200 and wrote a child
    /// under a name the operator never saw. Here the preview approves the disambiguated
    /// "rig-div (rig-div-vnet)"; the rename removes the collision, so the plan would now write the
    /// bare "rig-div".
    /// </remarks>
    [Fact]
    public async Task BulkCreateFromAzurePlan_ChildNamesMovedByAConcurrentRename_IsRefused()
    {
        // An existing Bastet subnet that the VNet prefix matches exactly, whose name collides with
        // the incoming Azure child and so forces the child to be disambiguated.
        await AddSubnetAsync("rig-div", "10.151.0.0", 16);
        int existingId = (await _context.Subnets.SingleAsync(TestContext.Current.CancellationToken)).Id;

        BulkImportSelectionDto selection = Selection(new BulkImportExpectedTargetDto
        {
            TargetType = nameof(BulkImportTargetType.ExactMatch),
            ExistingTargetSubnetId = existingId,
            WillRename = false,
            WillMarkFullyAllocated = false,
            // What the preview displayed, with the child disambiguated against the target's name.
            ChildNames = ["rig-div (rig-div)"]
        });
        selection.VNetPrefixes[0].Subnets =
        [
            new BulkImportSelectedSubnetDto
            {
                Name = "rig-div",
                AddressPrefix = "10.151.1.0/24",
                AzureResourceId = VNetId + "/subnets/rig-div"
            }
        ];

        // The second admin renames the target, so the collision - and the disambiguation - is gone.
        Subnet target = await _context.Subnets.SingleAsync(TestContext.Current.CancellationToken);
        target.Name = "renamed-by-someone-else";
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IActionResult result = await Commit(selection);

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("child subnet names have changed",
            System.Text.Json.JsonSerializer.Serialize(conflict.Value), StringComparison.Ordinal);

        // Nothing was written: the child does not exist under either name.
        Assert.Equal(1, await _context.Subnets.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A caller that did not preview still carries no child names, and must not be refused for it.
    /// </summary>
    [Fact]
    public async Task BulkCreateFromAzurePlan_NoChildNamesSupplied_IsNotRefused()
    {
        BulkImportSelectionDto selection = Selection(ApprovedNewTopLevel());
        Assert.Null(selection.VNetPrefixes[0].Expected!.ChildNames);

        IActionResult result = await Commit(selection);

        _ = Assert.IsType<OkObjectResult>(result);
    }

    /// <summary>
    /// A malformed body must come back as the planner's modelled 400, not as an unhandled 500.
    /// </summary>
    /// <remarks>
    /// Round-11 K3. The approved-plan comparison walks <c>selection.VNetPrefixes</c> and reads each
    /// element before any null guard. System.Text.Json overwrites a collection initialiser with null
    /// when the body carries an explicit null, and a list element can itself be null - the planner
    /// guards both and records a global error, but this walk runs *before* the CanCommit check that
    /// reports them, so it has to survive them. Without the guard both shapes threw
    /// NullReferenceException from a point outside the transaction's try, where the action's own
    /// catch handles only TimeoutException: the documented direct JSON API stopped returning JSON at
    /// all, and the wizard fell back to the literal string "Server error: 500", losing the planner's
    /// actual message.
    /// </remarks>
    [Fact]
    public async Task BulkCreateFromAzurePlan_NullPrefixCollection_IsABadRequestNotAServerError()
    {
        BulkImportSelectionDto selection = Selection(ApprovedNewTopLevel());
        selection.VNetPrefixes = null!;

        IActionResult result = await Commit(selection);

        BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("No VNet address prefixes were selected.",
            System.Text.Json.JsonSerializer.Serialize(bad.Value), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BulkCreateFromAzurePlan_NullPrefixElement_IsABadRequestNotAServerError()
    {
        BulkImportSelectionDto selection = Selection(ApprovedNewTopLevel());
        selection.VNetPrefixes.Add(null!);

        IActionResult result = await Commit(selection);

        BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("A selected VNet prefix was empty.",
            System.Text.Json.JsonSerializer.Serialize(bad.Value), StringComparison.Ordinal);
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
