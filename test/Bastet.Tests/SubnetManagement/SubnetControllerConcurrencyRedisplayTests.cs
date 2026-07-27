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

/// <summary>
/// Pins the value the Edit form redisplays after losing an optimistic-concurrency race: it must be
/// the value actually in the database, not the wall clock at the moment of the failed save. Both
/// AsNoTracking() calls are load-bearing, and the fall-through repopulation is the one that reaches
/// the view.
///
/// Round 5 recorded that this could not be tested under the suite's provider, because
/// [Timestamp] byte[] RowVersion is only store-generated on SQL Server. The premise is true and the
/// inference is not: the Edit POST supplies the original token itself
/// (SubnetController.Edit.cs - context.Entry(subnet).OriginalValues["RowVersion"] = viewModel.RowVersion),
/// so the comparison is an ordinary WHERE clause SQLite evaluates fine.
///
/// One provider caveat to keep in mind before extending this: under SQLite the stored token is NULL,
/// so *any* non-null posted RowVersion conflicts. That reaches the handler faithfully but does not
/// reproduce production's value-versus-value comparison - do not read a pass here as proof of the
/// SQL Server path.
/// </summary>
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
        // Arrange: a subnet whose stored RowVersion is a known blob, so a posted token that does not
        // match it makes the UPDATE match zero rows.
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

        // The provider caveat above, made executable: SQLite stores no token, so the posted one
        // below conflicts by being non-null rather than by differing in value.
        byte[]? stored = await _context.Subnets.AsNoTracking()
            .Where(s => s.Id == 50).Select(s => s.RowVersion)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.Null(stored);

        _context.ChangeTracker.Clear();

        // Act: the operator posts an edit carrying a stale/foreign concurrency token.
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

        // The concurrency handler must have been the one that ran.
        ViewResult view = Assert.IsType<ViewResult>(result);
        EditSubnetViewModel shown = Assert.IsType<EditSubnetViewModel>(view.Model);
        Assert.Contains(_controller.ModelState.Values.SelectMany(v => v.Errors),
            e => e.ErrorMessage.Contains("modified by another user"));

        // The screen must show the value that is actually in the database.
        Assert.Equal(OtherUsersSave, shown.LastModifiedAt);
    }
}
