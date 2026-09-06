using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Locking;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.HostIpManagement;

public class HostIpEditRedisplayTests : IDisposable
{
    private sealed class AlwaysTimingOutLockService : ISubnetLockingService
    {
        public Task<T> ExecuteWithSubnetLockAsync<T>(Func<Task<T>> operation) =>
            throw new TimeoutException("Could not acquire subnet operation lock");
    }

    private readonly SqliteConnection _connection;
    private readonly BastetDbContext _context;
    private readonly IIpUtilityService _ip = new IpUtilityService();
    private readonly DateTime _storedCreatedAt;

    public HostIpEditRedisplayTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _context = new BastetDbContext(new DbContextOptionsBuilder<BastetDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();

        _context.Subnets.Add(new Subnet { Id = 1, Name = "leaf", NetworkAddress = "10.0.9.0", Cidr = 24, CreatedAt = DateTime.UtcNow, CreatedBy = "t" });
        _context.HostIpAssignments.Add(new HostIpAssignment { IP = "10.0.9.5", Name = "host", SubnetId = 1, CreatedAt = DateTime.UtcNow, CreatedBy = "t" });
        _context.SaveChanges();
        _storedCreatedAt = _context.HostIpAssignments.AsNoTracking().Single(h => h.IP == "10.0.9.5").CreatedAt;
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private HostIpController CreateController(ISubnetLockingService locking)
    {
        HostIpController controller = new(_context, new HostIpValidationService(_ip, _context), _ip,
            ControllerTestHelper.CreateMockUserContextService(), locking, NullLogger<HostIpController>.Instance);
        ControllerTestHelper.SetupController(controller);
        return controller;
    }

    private static EditHostIpViewModel PostedForm() => new()
    {
        IP = "10.0.9.5",
        Name = "renamed",
        SubnetId = 1,
        RowVersion = [1, 2, 3]
    };

    [Fact]
    public async Task Edit_LockTimesOut_RedisplaysTheStoredSubnetAndCreatedAt()
    {
        HostIpController controller = CreateController(new AlwaysTimingOutLockService());

        ViewResult view = Assert.IsType<ViewResult>(await controller.Edit("10.0.9.5", PostedForm()));
        EditHostIpViewModel model = Assert.IsType<EditHostIpViewModel>(view.Model);

        Assert.Contains(view.ViewData.ModelState[""]!.Errors, e => e.ErrorMessage.Contains("timed out"));
        Assert.Equal("leaf (10.0.9.0/24)", model.SubnetInfo);
        Assert.Equal(_storedCreatedAt, model.CreatedAt);
    }

    [Fact]
    public async Task Edit_ModelStateInvalid_RedisplaysTheStoredSubnetAndCreatedAt()
    {
        HostIpController controller = CreateController(ControllerTestHelper.CreateMockSubnetLockingService());
        controller.ModelState.AddModelError("Name", "HTML tags are not allowed in host names");

        ViewResult view = Assert.IsType<ViewResult>(await controller.Edit("10.0.9.5", PostedForm()));
        EditHostIpViewModel model = Assert.IsType<EditHostIpViewModel>(view.Model);

        Assert.Equal("leaf (10.0.9.0/24)", model.SubnetInfo);
        Assert.Equal(_storedCreatedAt, model.CreatedAt);
    }

    [Fact]
    public async Task Edit_StaleRowVersion_RedisplaysTheStoredSubnetAndCreatedAt()
    {
        HostIpController controller = CreateController(ControllerTestHelper.CreateMockSubnetLockingService());

        ViewResult view = Assert.IsType<ViewResult>(await controller.Edit("10.0.9.5", PostedForm()));
        EditHostIpViewModel model = Assert.IsType<EditHostIpViewModel>(view.Model);

        Assert.False(view.ViewData.ModelState.IsValid);
        Assert.Equal("leaf (10.0.9.0/24)", model.SubnetInfo);
        Assert.Equal(_storedCreatedAt, model.CreatedAt);
    }
}
