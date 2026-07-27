using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.HostIpManagement;

/// <summary>
/// A subnet holds either child subnets or host IPs, never both, so the host-IP list has to turn away
/// a subnet that has children. The check needs ChildSubnets loaded: the collection defaults to empty
/// and there is no lazy loading, so without the Include it silently never fires.
/// </summary>
public class HostIpIndexGuardTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly HostIpController _controller;

    public HostIpIndexGuardTests()
    {
        DbContextOptions<BastetDbContext> options = new DbContextOptionsBuilder<BastetDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new BastetDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        IIpUtilityService ipUtilityService = new IpUtilityService();
        _controller = new HostIpController(
            _context,
            new HostIpValidationService(ipUtilityService, _context),
            ipUtilityService,
            ControllerTestHelper.CreateMockUserContextService(),
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<HostIpController>.Instance);
        ControllerTestHelper.SetupController(_controller);

        _context.Subnets.AddRange(
            new Subnet { Id = 1, Name = "Parent", NetworkAddress = "10.0.0.0", Cidr = 16, CreatedAt = DateTime.UtcNow, CreatedBy = "t" },
            new Subnet { Id = 2, Name = "Child", NetworkAddress = "10.0.1.0", Cidr = 24, ParentSubnetId = 1, CreatedAt = DateTime.UtcNow, CreatedBy = "t" },
            new Subnet { Id = 3, Name = "Leaf", NetworkAddress = "10.0.2.0", Cidr = 24, CreatedAt = DateTime.UtcNow, CreatedBy = "t" },
            new Subnet { Id = 4, Name = "Allocated", NetworkAddress = "10.0.3.0", Cidr = 24, IsFullyAllocated = true, CreatedAt = DateTime.UtcNow, CreatedBy = "t" });
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Index_SubnetWithChildSubnets_RedirectsWithExplanation()
    {
        // Mirrors a fresh request: nothing this test tracked is visible to the controller, so the
        // navigation collection is only populated if the query asks for it.
        _context.ChangeTracker.Clear();

        IActionResult result = await _controller.Index(1);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal("Subnet", redirect.ControllerName);
        Assert.Equal(1, redirect.RouteValues?["id"]);
        Assert.Contains("cannot have host IP assignments", _controller.TempData["ErrorMessage"]?.ToString());
    }

    [Fact]
    public async Task Index_FullyAllocatedSubnet_RedirectsWithExplanation()
    {
        _context.ChangeTracker.Clear();

        IActionResult result = await _controller.Index(4);

        _ = Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("cannot have host IP assignments", _controller.TempData["ErrorMessage"]?.ToString());
    }

    [Fact]
    public async Task Index_LeafSubnet_ShowsTheHostIpList()
    {
        _context.ChangeTracker.Clear();

        IActionResult result = await _controller.Index(3);

        ViewResult view = Assert.IsType<ViewResult>(result);
        Assert.Equal(3, view.ViewData["SubnetId"]);
    }

    // -------------------------------------------------------------------------
    // The same guard on Create. A redirect starts a new request, so anything put in ModelState is
    // gone by the time Details renders - and Details reads only TempData.
    // -------------------------------------------------------------------------

    /// <summary>
    /// The reachable route is the one the tag helper emits: GET /HostIp/Create?subnetId=1. A
    /// hand-typed /HostIp/Create/1 binds id, leaves subnetId at 0 and returns NotFound before the
    /// guard is ever reached.
    /// </summary>
    [Fact]
    public async Task Create_SubnetWithChildSubnets_RedirectsWithExplanation()
    {
        _context.ChangeTracker.Clear();

        IActionResult result = await _controller.Create(1);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal("Subnet", redirect.ControllerName);
        Assert.Equal(1, redirect.RouteValues?["id"]);
        Assert.False(string.IsNullOrWhiteSpace(_controller.TempData["ErrorMessage"] as string));
    }

    [Fact]
    public async Task Create_FullyAllocatedSubnet_RedirectsWithExplanation()
    {
        _context.ChangeTracker.Clear();

        IActionResult result = await _controller.Create(4);

        _ = Assert.IsType<RedirectToActionResult>(result);
        Assert.False(string.IsNullOrWhiteSpace(_controller.TempData["ErrorMessage"] as string));
    }

    [Fact]
    public async Task Create_LeafSubnet_ShowsTheForm()
    {
        _context.ChangeTracker.Clear();

        IActionResult result = await _controller.Create(3);

        _ = Assert.IsType<ViewResult>(result);
        Assert.False(_controller.TempData.ContainsKey("ErrorMessage"));
    }
}
