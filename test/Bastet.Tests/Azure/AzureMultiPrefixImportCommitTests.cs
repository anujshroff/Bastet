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

namespace Bastet.Tests.Azure;

/// <summary>
/// The single-VNet import wizard's commit path, driven with the two rows an Azure subnet owning two
/// IPv4 prefixes now produces. Both must be created, and their names must not collide: Subnet.Name
/// carries a NON-unique index, so two identically-named rows would persist silently and be
/// indistinguishable in every list and dropdown.
///
/// The browser is not the authority here. These tests post the rows directly, which is also what a
/// crafted or replayed request looks like, so the disambiguation has to be server-side.
/// </summary>
[Collection(AzureFeatureFlagCollection.Name)]
public class AzureMultiPrefixImportCommitTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly SubnetController _controller;

    private const string VNetId =
        "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/multi-vnet";
    private const string SubnetResourceId = $"{VNetId}/subnets/sn-multi";

    public AzureMultiPrefixImportCommitTests()
    {
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "true");

        DbContextOptions<BastetDbContext> options = new DbContextOptionsBuilder<BastetDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new BastetDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        IIpUtilityService ip = new IpUtilityService();
        IUserContextService users = ControllerTestHelper.CreateMockUserContextService();

        _controller = new SubnetController(
            _context,
            ip,
            new SubnetValidationService(ip),
            new HostIpValidationService(ip, _context),
            users,
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<SubnetController>.Instance);

        ControllerTestHelper.SetupController(_controller);
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        _controller.HttpContext.Request.Headers.Referer = "https://localhost/Azure/Import/1";

        // A /16 target that comfortably contains both prefixes of the Azure subnet.
        _context.Subnets.Add(new Subnet
        {
            Id = 1,
            Name = "Target",
            NetworkAddress = "10.31.0.0",
            Cidr = 16,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
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

    private static AzureImportSubnetViewModel Row(string name, string network, int cidr) =>
        new()
        {
            Name = name,
            NetworkAddress = network,
            Cidr = cidr,
            Description = "Imported from Azure VNet: multi-vnet",
            Tags = "Azure",
            ParentSubnetId = 1,
            AzureResourceId = SubnetResourceId
        };

    /// <summary>
    /// Both prefixes of one Azure subnet are created, and each Bastet row is named for the range it
    /// actually holds rather than both taking the bare Azure name.
    /// </summary>
    [Fact]
    public async Task BothPrefixesOfOneAzureSubnet_AreCreatedWithDistinctNames()
    {
        List<AzureImportSubnetViewModel> subnets =
        [
            Row("sn-multi", "10.31.0.0", 24),
            Row("sn-multi", "10.31.1.0", 24)
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(
            1, subnets, "multi-vnet", VNetId, isAzureImport: true);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);

        List<Subnet> created = await _context.Subnets
            .Where(s => s.ParentSubnetId == 1)
            .OrderBy(s => s.NetworkAddress)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, created.Count);
        Assert.Equal(["10.31.0.0", "10.31.1.0"], created.Select(s => s.NetworkAddress));

        // The names must differ, and each must name its own range.
        Assert.Equal(2, created.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("sn-multi (10.31.0.0/24)", created[0].Name);
        Assert.Equal("sn-multi (10.31.1.0/24)", created[1].Name);

        // Both stay linked to the one Azure subnet they came from.
        Assert.All(created, s => Assert.Equal(SubnetResourceId, s.AzureResourceId));
    }

    /// <summary>
    /// The guard for every existing install: a single-prefix import is byte-for-byte what it was.
    /// No suffix appears when there is nothing to disambiguate.
    /// </summary>
    [Fact]
    public async Task SinglePrefixImport_KeepsThePlainAzureName()
    {
        List<AzureImportSubnetViewModel> subnets = [Row("web", "10.31.5.0", 24)];

        await _controller.BatchCreateChildSubnets(1, subnets, "multi-vnet", VNetId, isAzureImport: true);

        Subnet created = Assert.Single(await _context.Subnets
            .Where(s => s.ParentSubnetId == 1)
            .ToListAsync(TestContext.Current.CancellationToken));

        Assert.Equal("web", created.Name);
    }

    /// <summary>
    /// Two genuinely different Azure subnets that happen to share a name must also stop colliding -
    /// same non-unique index, same consequence. They are distinguished by their own prefixes.
    /// </summary>
    [Fact]
    public async Task TwoDifferentAzureSubnetsSharingAName_AreAlsoDisambiguated()
    {
        AzureImportSubnetViewModel a = Row("web", "10.31.6.0", 24);
        AzureImportSubnetViewModel b = Row("web", "10.31.7.0", 24);
        b.AzureResourceId = $"{VNetId}/subnets/web-b";

        await _controller.BatchCreateChildSubnets(1, [a, b], "multi-vnet", VNetId, isAzureImport: true);

        List<Subnet> created = await _context.Subnets
            .Where(s => s.ParentSubnetId == 1)
            .OrderBy(s => s.NetworkAddress)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, created.Count);
        Assert.Equal(2, created.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// Overlap validation still runs across the batch: a second row covering the same range as the
    /// first is refused, not silently renamed and created. Disambiguating names must not become a
    /// way to smuggle in a duplicate allocation.
    /// </summary>
    [Fact]
    public async Task TwoRowsCoveringTheSameRange_AreStillRefused()
    {
        List<AzureImportSubnetViewModel> subnets =
        [
            Row("sn-multi", "10.31.0.0", 24),
            Row("sn-multi", "10.31.0.0", 24)
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(
            1, subnets, "multi-vnet", VNetId, isAzureImport: true);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.True(_controller.TempData.ContainsKey("ErrorMessage"));

        Assert.Empty(await _context.Subnets
            .Where(s => s.ParentSubnetId == 1)
            .ToListAsync(TestContext.Current.CancellationToken));
    }
}
