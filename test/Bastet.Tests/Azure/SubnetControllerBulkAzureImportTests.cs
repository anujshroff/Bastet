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

        BulkImportSelectionDto selection = Selection(ApprovedNewTopLevel());

        await InterleaveHandCreatedSubnetAsync();

        IActionResult result = await Commit(selection);

        _ = Assert.IsType<ConflictObjectResult>(result);

        Subnet reserved = await _context.Subnets.SingleAsync(
            s => s.Name == "Finance-Prod-Reserved", TestContext.Current.CancellationToken);
        Assert.Null(reserved.AzureResourceId);

        Assert.Equal(1, await _context.Subnets.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BulkCreateFromAzurePlan_TargetAdoptedAfterPreview_ConflictNamesTheDivergence()
    {
        BulkImportSelectionDto selection = Selection(ApprovedNewTopLevel());
        await InterleaveHandCreatedSubnetAsync();

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(await Commit(selection));

        string body = System.Text.Json.JsonSerializer.Serialize(conflict.Value);
        Assert.Contains("10.151.0.0/16", body, StringComparison.Ordinal);
        Assert.Contains(nameof(BulkImportTargetType.ExactMatch), body, StringComparison.Ordinal);

        Assert.DoesNotContain("Reserved by the network team", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BulkCreateFromAzurePlan_PlanUnchangedSincePreview_StillCommits()
    {

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
    public async Task BulkCreateFromAzurePlan_ChildNamesMovedByAConcurrentRename_IsRefused()
    {

        await AddSubnetAsync("rig-div", "10.151.0.0", 16);
        int existingId = (await _context.Subnets.SingleAsync(TestContext.Current.CancellationToken)).Id;

        BulkImportSelectionDto selection = Selection(new BulkImportExpectedTargetDto
        {
            TargetType = nameof(BulkImportTargetType.ExactMatch),
            ExistingTargetSubnetId = existingId,
            WillRename = false,
            WillMarkFullyAllocated = false,

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

        Subnet target = await _context.Subnets.SingleAsync(TestContext.Current.CancellationToken);
        target.Name = "renamed-by-someone-else";
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IActionResult result = await Commit(selection);

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("child subnet names have changed",
            System.Text.Json.JsonSerializer.Serialize(conflict.Value), StringComparison.Ordinal);

        Assert.Equal(1, await _context.Subnets.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BulkCreateFromAzurePlan_NoChildNamesSupplied_IsNotRefused()
    {
        BulkImportSelectionDto selection = Selection(ApprovedNewTopLevel());
        Assert.Null(selection.VNetPrefixes[0].Expected!.ChildNames);

        IActionResult result = await Commit(selection);

        _ = Assert.IsType<OkObjectResult>(result);
    }

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

        await InterleaveHandCreatedSubnetAsync();

        IActionResult result = await Commit(Selection(expected: null));

        _ = Assert.IsType<OkObjectResult>(result);
    }
}
