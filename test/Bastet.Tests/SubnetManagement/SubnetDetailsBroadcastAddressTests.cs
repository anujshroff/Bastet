using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.SubnetManagement;

/// <summary>
/// The Subnet Details card must not name an address the application will happily assign as the
/// subnet's broadcast address.
/// </summary>
/// <remarks>
/// Regression for round-11 K5. <c>HostIpValidationService</c> applies the network/broadcast
/// reservation only when <c>Cidr &lt; 31</c>, so both /31 addresses and the single /32 address are
/// legitimately assignable - and the details page was computing a broadcast address for them
/// unconditionally. On a /31 with both addresses assigned the page stated "Usable IP Addresses 2",
/// named one of those two the broadcast, and listed that same address as a host IP assignment
/// directly below. On a /32 the Network Address and Broadcast Address rows printed the identical
/// string.
/// </remarks>
public class SubnetDetailsBroadcastAddressTests
{
    private static SubnetController CreateController(BastetDbContext context)
    {
        IIpUtilityService ip = new IpUtilityService();
        SubnetController controller = new(
            context,
            ip,
            new SubnetValidationService(ip),
            new HostIpValidationService(ip, context),
            ControllerTestHelper.CreateMockUserContextService(),
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<SubnetController>.Instance);
        ControllerTestHelper.SetupController(controller);
        return controller;
    }

    private static async Task<SubnetDetailsViewModel> DetailsFor(string network, int cidr)
    {
        using BastetDbContext context = TestDbContextFactory.CreateDbContext();
        context.Subnets.Add(new Subnet
        {
            Id = 1,
            Name = "under-test",
            NetworkAddress = network,
            Cidr = cidr,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IActionResult result = await CreateController(context).Details(1);

        ViewResult view = Assert.IsType<ViewResult>(result);
        return Assert.IsType<SubnetDetailsViewModel>(view.Model);
    }

    [Theory]
    [InlineData("10.211.0.0", 31)]
    [InlineData("10.212.0.0", 32)]
    public async Task Details_AtOrAbove31_HasNoBroadcastAddress(string network, int cidr)
    {
        SubnetDetailsViewModel model = await DetailsFor(network, cidr);

        Assert.Equal(string.Empty, model.BroadcastAddress);
    }

    /// <summary>
    /// The guard must not reach below /31, where a broadcast address is real and is enforced -
    /// assigning it as a host IP is refused.
    /// </summary>
    [Theory]
    [InlineData("10.213.0.0", 30, "10.213.0.3")]
    [InlineData("10.214.0.0", 24, "10.214.0.255")]
    public async Task Details_Below31_StillReportsTheBroadcastAddress(string network, int cidr, string expected)
    {
        SubnetDetailsViewModel model = await DetailsFor(network, cidr);

        Assert.Equal(expected, model.BroadcastAddress);
    }
}
