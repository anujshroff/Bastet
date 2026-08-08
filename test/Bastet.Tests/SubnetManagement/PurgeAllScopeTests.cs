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

public class PurgeAllScopeTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly SubnetController _subnetController;
    private readonly HostIpController _hostIpController;

    public PurgeAllScopeTests()
    {
        _context = TestDbContextFactory.CreateDbContext();

        IUserContextService userContext = ControllerTestHelper.CreateMockUserContextService();
        IIpUtilityService ipUtility = new IpUtilityService();
        SubnetValidationService subnetValidation = new(ipUtility);
        HostIpValidationService hostIpValidation = new(ipUtility, _context);

        _subnetController = new SubnetController(
            _context, ipUtility, subnetValidation, hostIpValidation, userContext,
            ControllerTestHelper.CreateMockSubnetLockingService(), NullLogger<SubnetController>.Instance);
        ControllerTestHelper.SetupController(_subnetController);

        _hostIpController = new HostIpController(
            _context, hostIpValidation, ipUtility, userContext,
            ControllerTestHelper.CreateMockSubnetLockingService(), NullLogger<HostIpController>.Instance);
        ControllerTestHelper.SetupController(_hostIpController);
    }

    private DeletedSubnet ArchiveSubnet(string name, string network)
    {
        DeletedSubnet row = new()
        {
            Name = name,
            NetworkAddress = network,
            Cidr = 24,
            DeletedAt = DateTime.UtcNow,
            DeletedBy = "test-user",
            CreatedAt = DateTime.UtcNow
        };
        _context.DeletedSubnets.Add(row);
        _context.SaveChanges();
        return row;
    }

    private DeletedHostIpAssignment ArchiveHostIp(string ip)
    {
        DeletedHostIpAssignment row = new()
        {
            OriginalIP = ip,
            Name = "host-" + ip,
            OriginalSubnetId = 1,
            DeletedAt = DateTime.UtcNow,
            DeletedBy = "test-user",
            CreatedAt = DateTime.UtcNow
        };
        _context.DeletedHostIpAssignments.Add(row);
        _context.SaveChanges();
        return row;
    }

    [Fact]
    public async Task PurgeAllSubnets_DestroysOnlyWhatTheConfirmationPageCounted()
    {
        DeletedSubnet shown = ArchiveSubnet("solo", "10.90.0.0");

        ViewResult page = Assert.IsType<ViewResult>(await _subnetController.PurgeAllDeletedSubnets());
        PurgeAllDeletedSubnetsViewModel model =
            Assert.IsType<PurgeAllDeletedSubnetsViewModel>(page.Model);
        Assert.Equal(1, model.Count);
        Assert.Equal(shown.Id, model.MaxId);

        DeletedSubnet later1 = ArchiveSubnet("big", "10.50.0.0");
        DeletedSubnet later2 = ArchiveSubnet("child", "10.50.1.0");

        await _subnetController.PurgeAllDeletedSubnetsConfirmed("approved", model.MaxId);

        List<int> remaining = [.. _context.DeletedSubnets.Select(d => d.Id)];
        Assert.Equal([later1.Id, later2.Id], [.. remaining.OrderBy(i => i)]);
        Assert.Equal("Permanently purged 1 deleted subnet record(s).",
            _subnetController.TempData["SuccessMessage"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PurgeAllSubnets_WithoutAScope_RefusesAndDestroysNothing(int? confirmedMaxId)
    {
        ArchiveSubnet("a", "10.1.0.0");
        ArchiveSubnet("b", "10.2.0.0");

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(
            await _subnetController.PurgeAllDeletedSubnetsConfirmed("approved", confirmedMaxId));

        Assert.Equal(nameof(SubnetController.PurgeAllDeletedSubnets), redirect.ActionName);
        Assert.Equal(2, _context.DeletedSubnets.Count());
        Assert.Null(_subnetController.TempData["SuccessMessage"]);
        Assert.NotNull(_subnetController.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task PurgeAllSubnets_WhenNothingChanged_StillPurgesEverything()
    {
        ArchiveSubnet("a", "10.1.0.0");
        ArchiveSubnet("b", "10.2.0.0");

        ViewResult page = Assert.IsType<ViewResult>(await _subnetController.PurgeAllDeletedSubnets());
        PurgeAllDeletedSubnetsViewModel model =
            Assert.IsType<PurgeAllDeletedSubnetsViewModel>(page.Model);

        await _subnetController.PurgeAllDeletedSubnetsConfirmed("approved", model.MaxId);

        Assert.Empty(_context.DeletedSubnets);
        Assert.Equal("Permanently purged 2 deleted subnet record(s).",
            _subnetController.TempData["SuccessMessage"]);
    }

    [Fact]
    public async Task PurgeAllHostIps_DestroysOnlyWhatTheConfirmationPageCounted()
    {
        DeletedHostIpAssignment shown = ArchiveHostIp("10.60.0.5");

        ViewResult page = Assert.IsType<ViewResult>(await _hostIpController.PurgeAllDeletedHostIps());
        PurgeAllDeletedHostIpsViewModel model =
            Assert.IsType<PurgeAllDeletedHostIpsViewModel>(page.Model);
        Assert.Equal(1, model.Count);
        Assert.Equal(shown.Id, model.MaxId);

        DeletedHostIpAssignment later = ArchiveHostIp("10.60.0.6");

        await _hostIpController.PurgeAllDeletedHostIpsConfirmed("approved", model.MaxId);

        int remaining = Assert.Single(_context.DeletedHostIpAssignments.Select(d => d.Id));
        Assert.Equal(later.Id, remaining);
        Assert.Equal("Permanently purged 1 deleted host IP record(s).",
            _hostIpController.TempData["SuccessMessage"]);
    }

    [Fact]
    public async Task PurgeAllHostIps_WithoutAScope_RefusesAndDestroysNothing()
    {
        ArchiveHostIp("10.60.0.5");

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(
            await _hostIpController.PurgeAllDeletedHostIpsConfirmed("approved", null));

        Assert.Equal(nameof(HostIpController.PurgeAllDeletedHostIps), redirect.ActionName);
        Assert.Single(_context.DeletedHostIpAssignments);
        Assert.Null(_hostIpController.TempData["SuccessMessage"]);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
