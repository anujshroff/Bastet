using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Locking;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.SubnetManagement;

public class SubnetLockTimeoutTests : IDisposable
{

    private sealed class AlwaysTimingOutLockService : ISubnetLockingService
    {
        public Task<T> ExecuteWithSubnetLockAsync<T>(Func<Task<T>> operation) =>
            throw new TimeoutException("Could not acquire subnet operation lock");
    }

    private sealed class TimesOutAfterAnotherOperation(Action anotherOperation) : ISubnetLockingService
    {
        public Task<T> ExecuteWithSubnetLockAsync<T>(Func<Task<T>> operation)
        {
            anotherOperation();
            throw new TimeoutException("Could not acquire subnet operation lock");
        }
    }

    private readonly BastetDbContext _context;
    private readonly IIpUtilityService _ipUtilityService = new IpUtilityService();

    public SubnetLockTimeoutTests()
    {
        _context = TestDbContextFactory.CreateDbContext();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }


    [Fact]
    public async Task HostIpCreate_LockTimesOut_ReturnsViewWithFriendlyError()
    {
        HostIpController controller = new(_context, new HostIpValidationService(_ipUtilityService, _context),
            _ipUtilityService, ControllerTestHelper.CreateMockUserContextService(),
            new AlwaysTimingOutLockService(), NullLogger<HostIpController>.Instance);
        ControllerTestHelper.SetupController(controller);

        IActionResult result = await controller.Create(new CreateHostIpViewModel
        {
            IP = "10.0.0.5",
            Name = "host",
            SubnetId = 1
        });

        ViewResult view = Assert.IsType<ViewResult>(result);
        Assert.False(view.ViewData.ModelState.IsValid);
        Assert.Contains(view.ViewData.ModelState[""]!.Errors, e => e.ErrorMessage.Contains("timed out"));
    }

    [Fact]
    public async Task SubnetDeleteConfirmed_TheRowIsDeletedWhileWaitingForTheLock_AnswersTheErrorPageAndStashesNothing()
    {
        AddSubnetWithHostIp();
        SubnetController controller = SubnetControllerWith(new TimesOutAfterAnotherOperation(() =>
        {
            _context.HostIpAssignments.RemoveRange(_context.HostIpAssignments);
            _context.Subnets.RemoveRange(_context.Subnets);
            _context.SaveChanges();
        }));

        IActionResult result = await controller.DeleteConfirmed(1, "approved", confirmedMaxSubnetId: 0, confirmedHostIpCount: 1);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Error", redirect.ControllerName);
        Assert.False(controller.TempData.ContainsKey("ErrorMessage"));
    }

    [Fact]
    public async Task SubnetDeleteConfirmed_TheLockTimesOutOnALiveRow_StillAsksForARetryOnTheDeletePage()
    {
        AddSubnetWithHostIp();
        SubnetController controller = SubnetControllerWith(new AlwaysTimingOutLockService());

        IActionResult result = await controller.DeleteConfirmed(1, "approved", confirmedMaxSubnetId: 0, confirmedHostIpCount: 1);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Delete", redirect.ActionName);
        Assert.Contains("timed out", controller.TempData["ErrorMessage"] as string);
    }

    [Fact]
    public async Task HostIpDeleteConfirmed_TheRowIsDeletedWhileWaitingForTheLock_Answers404AndStashesNothing()
    {
        AddSubnetWithHostIp();
        HostIpController controller = HostIpControllerWith(new TimesOutAfterAnotherOperation(() =>
        {
            _context.HostIpAssignments.RemoveRange(_context.HostIpAssignments);
            _context.SaveChanges();
        }));

        IActionResult result = await controller.DeleteConfirmed("10.0.0.5", "approved");

        _ = Assert.IsType<NotFoundResult>(result);
        Assert.False(controller.TempData.ContainsKey("ErrorMessage"));
    }

    [Fact]
    public async Task HostIpDeleteConfirmed_TheLockTimesOutOnALiveRow_StillAsksForARetryOnTheDeletePage()
    {
        AddSubnetWithHostIp();
        HostIpController controller = HostIpControllerWith(new AlwaysTimingOutLockService());

        IActionResult result = await controller.DeleteConfirmed("10.0.0.5", "approved");

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Delete", redirect.ActionName);
        Assert.Contains("timed out", controller.TempData["ErrorMessage"] as string);
    }

    private void AddSubnetWithHostIp()
    {
        _context.Subnets.Add(new Subnet
        {
            Id = 1,
            Name = "root",
            NetworkAddress = "10.0.0.0",
            Cidr = 24,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        _context.HostIpAssignments.Add(new HostIpAssignment
        {
            IP = "10.0.0.5",
            Name = "box",
            SubnetId = 1,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        _context.SaveChanges();
    }

    private SubnetController SubnetControllerWith(ISubnetLockingService lockingService)
    {
        SubnetController controller = new(_context, _ipUtilityService, new SubnetValidationService(_ipUtilityService),
            new HostIpValidationService(_ipUtilityService, _context), ControllerTestHelper.CreateMockUserContextService(),
            lockingService, NullLogger<SubnetController>.Instance);
        ControllerTestHelper.SetupController(controller);
        return controller;
    }

    private HostIpController HostIpControllerWith(ISubnetLockingService lockingService)
    {
        HostIpController controller = new(_context, new HostIpValidationService(_ipUtilityService, _context),
            _ipUtilityService, ControllerTestHelper.CreateMockUserContextService(), lockingService,
            NullLogger<HostIpController>.Instance);
        ControllerTestHelper.SetupController(controller);
        return controller;
    }
}
