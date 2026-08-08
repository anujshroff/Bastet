using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.SubnetManagement;

public class SubnetControllerConcurrencyRedisplayTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly SubnetController _controller;

    public SubnetControllerConcurrencyRedisplayTests()
    {
        _context = TestDbContextFactory.CreateDbContext();
        IIpUtilityService ip = new IpUtilityService();
        _controller = new SubnetController(
            _context, ip, new SubnetValidationService(ip),
            new HostIpValidationService(ip, _context),
            ControllerTestHelper.CreateMockUserContextService(),
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<SubnetController>.Instance);
        ControllerTestHelper.SetupController(_controller);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private static readonly DateTime OtherUsersSave = new(2026, 01, 02, 10, 05, 00, DateTimeKind.Utc);

    [Fact]
    public async Task Edit_POST_ConcurrencyConflict_ShowsTheSavedLastModified_NotTheFailedAttempts()
    {

        _context.Subnets.Add(new Subnet
        {
            Id = 50,
            Name = "web",
            NetworkAddress = "10.50.0.0",
            Cidr = 24,
            CreatedAt = new DateTime(2026, 01, 01, 00, 00, 00, DateTimeKind.Utc),
            CreatedBy = "test-admin",
            LastModifiedAt = OtherUsersSave,
            ModifiedBy = "userB"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        byte[]? stored = await _context.Subnets.AsNoTracking()
            .Where(s => s.Id == 50).Select(s => s.RowVersion)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.Null(stored);

        _context.ChangeTracker.Clear();

        EditSubnetViewModel viewModel = new()
        {
            Id = 50,
            Name = "webA",
            NetworkAddress = "10.50.0.0",
            Cidr = 24,
            OriginalCidr = 24,
            RowVersion = [9, 9, 9, 9, 9, 9, 9, 9]
        };

        IActionResult result = await _controller.Edit(50, viewModel);

        ViewResult view = Assert.IsType<ViewResult>(result);
        EditSubnetViewModel shown = Assert.IsType<EditSubnetViewModel>(view.Model);
        Assert.Contains(_controller.ModelState.Values.SelectMany(v => v.Errors),
            e => e.ErrorMessage.Contains("modified by another user"));

        Assert.Equal(OtherUsersSave, shown.LastModifiedAt);
    }
}
