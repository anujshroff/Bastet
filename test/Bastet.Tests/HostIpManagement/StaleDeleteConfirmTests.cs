using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.HostIpManagement;

public class StaleDeleteConfirmTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly HostIpController _hostIpController;
    private readonly SubnetController _subnetController;

    public StaleDeleteConfirmTests()
    {
        _context = TestDbContextFactory.CreateDbContext();

        _context.Subnets.Add(new Subnet
        {
            Id = 1,
            Name = "root",
            NetworkAddress = "10.200.0.0",
            Cidr = 24,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        _context.SaveChanges();

        IIpUtilityService ipUtility = new IpUtilityService();

        _hostIpController = new HostIpController(
            _context,
            new HostIpValidationService(ipUtility, _context),
            ipUtility,
            ControllerTestHelper.CreateMockUserContextService(),
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<HostIpController>.Instance);
        ControllerTestHelper.SetupController(_hostIpController);

        _subnetController = new SubnetController(
            _context,
            ipUtility,
            new SubnetValidationService(ipUtility),
            new HostIpValidationService(ipUtility, _context),
            ControllerTestHelper.CreateMockUserContextService(),
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<SubnetController>.Instance);
        ControllerTestHelper.SetupController(_subnetController);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private void AddHostIp(string ip)
    {
        _context.HostIpAssignments.Add(new HostIpAssignment
        {
            IP = ip,
            Name = "box",
            SubnetId = 1,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        _context.SaveChanges();
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("nope")]
    public async Task HostIpDeleteConfirm_OnARowThatIsGone_Answers404AndStashesNothing(string confirmation)
    {
        IActionResult result = await _hostIpController.DeleteConfirmed("10.200.0.9", confirmation);

        _ = Assert.IsType<NotFoundResult>(result);
        Assert.False(_hostIpController.TempData.ContainsKey("ErrorMessage"));
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("nope")]
    public async Task SubnetDeleteConfirm_OnARowThatIsGone_AnswersTheErrorPageAndStashesNothing(string confirmation)
    {
        IActionResult result = await _subnetController.DeleteConfirmed(
            id: 99, confirmation, confirmedMaxSubnetId: 99, confirmedHostIpCount: 0);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Error", redirect.ControllerName);
        Assert.False(_subnetController.TempData.ContainsKey("ErrorMessage"));
    }

    [Fact]
    public async Task SubnetDeleteConfirm_OnARowThatIsGone_AnswersBeforeTheMissingScopeGate()
    {
        IActionResult result = await _subnetController.DeleteConfirmed(
            id: 99, "approved", confirmedMaxSubnetId: null, confirmedHostIpCount: null);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Error", redirect.ControllerName);
        Assert.False(_subnetController.TempData.ContainsKey("ErrorMessage"));
    }

    [Fact]
    public async Task HostIpDeleteConfirm_OnALiveRowWithTheWrongWord_StillRedisplaysTheDeletePage()
    {
        AddHostIp("10.200.0.9");

        IActionResult result = await _hostIpController.DeleteConfirmed("10.200.0.9", "nope");

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Delete", redirect.ActionName);
        Assert.Equal(
            "You must type 'approved' to confirm deletion.",
            _hostIpController.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task SubnetDeleteConfirm_OnALiveRowWithTheWrongWord_StillRedisplaysTheDeletePage()
    {
        IActionResult result = await _subnetController.DeleteConfirmed(
            id: 1, "nope", confirmedMaxSubnetId: 1, confirmedHostIpCount: 0);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Delete", redirect.ActionName);
        Assert.Equal(
            "You must type 'approved' to confirm deletion.",
            _subnetController.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task HostIpDeleteConfirm_OnALiveRow_StillDeletesIt()
    {
        AddHostIp("10.200.0.9");

        IActionResult result = await _hostIpController.DeleteConfirmed("10.200.0.9", "approved");

        _ = Assert.IsType<RedirectToActionResult>(result);
        Assert.Empty(_context.HostIpAssignments.Where(h => h.IP == "10.200.0.9"));
    }
}
