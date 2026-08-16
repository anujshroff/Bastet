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

    [Fact]
    public async Task Edit_POST_ConcurrencyConflict_NamesTheStoredValuesThatDiffer()
    {
        _context.Subnets.Add(new Subnet
        {
            Id = 52,
            Name = "web",
            NetworkAddress = "10.52.0.0",
            Cidr = 24,
            Tags = "prod",
            CreatedAt = new DateTime(2026, 01, 01, 00, 00, 00, DateTimeKind.Utc),
            CreatedBy = "test-admin"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        EditSubnetViewModel viewModel = new()
        {
            Id = 52,
            Name = "webA",
            NetworkAddress = "10.52.0.0",
            Cidr = 24,
            OriginalCidr = 24,
            Tags = "prod",
            RowVersion = [9, 9, 9, 9, 9, 9, 9, 9]
        };

        await _controller.Edit(52, viewModel);

        string message = Assert.Single(
            _controller.ModelState.Values.SelectMany(v => v.Errors),
            e => e.ErrorMessage.Contains("modified by another user")).ErrorMessage;
        Assert.Contains("Name is now 'web'", message);
        Assert.DoesNotContain("Tags", message);
    }

    [Fact]
    public async Task Edit_POST_ConcurrencyConflict_ReportsTheConflictExactlyOnce()
    {
        _context.Subnets.Add(new Subnet
        {
            Id = 53,
            Name = "db",
            NetworkAddress = "10.53.0.0",
            Cidr = 24,
            CreatedAt = new DateTime(2026, 01, 01, 00, 00, 00, DateTimeKind.Utc),
            CreatedBy = "test-admin"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await _context.Database.ExecuteSqlAsync(
            $"UPDATE Subnets SET RowVersion = {new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }} WHERE Id = 53",
            TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        EditSubnetViewModel viewModel = new()
        {
            Id = 53,
            Name = "dbA",
            NetworkAddress = "10.53.0.0",
            Cidr = 24,
            OriginalCidr = 24,
            RowVersion = [9, 9, 9, 9, 9, 9, 9, 9]
        };

        await _controller.Edit(53, viewModel);

        Assert.Single(
            _controller.ModelState.Values.SelectMany(v => v.Errors),
            e => e.ErrorMessage.Contains("modified by another user")
                || e.ErrorMessage.Contains("changed since this form was loaded"));
    }

    [Fact]
    public async Task Edit_POST_AStaleTokenOnAValidationFailurePath_NamesTheStoredValuesThatDiffer()
    {
        _context.Subnets.Add(new Subnet
        {
            Id = 54,
            Name = "cache",
            NetworkAddress = "10.54.0.0",
            Cidr = 24,
            CreatedAt = new DateTime(2026, 01, 01, 00, 00, 00, DateTimeKind.Utc),
            CreatedBy = "test-admin"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await _context.Database.ExecuteSqlAsync(
            $"UPDATE Subnets SET RowVersion = {new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }} WHERE Id = 54",
            TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        _controller.ModelState.AddModelError("Name", "forced invalid");
        EditSubnetViewModel viewModel = new()
        {
            Id = 54,
            Name = "cacheA",
            NetworkAddress = "10.54.0.0",
            Cidr = 24,
            OriginalCidr = 24,
            RowVersion = [9, 9, 9, 9, 9, 9, 9, 8]
        };

        await _controller.Edit(54, viewModel);

        string message = Assert.Single(
            _controller.ModelState.Values.SelectMany(v => v.Errors),
            e => e.ErrorMessage.Contains("modified by another user")).ErrorMessage;
        Assert.Contains("Name is now 'cache'", message);
    }

    [Fact]
    public async Task Edit_POST_ConcurrencyConflict_KeepsTheStaleToken_SoABlindRetryCannotOverwrite()
    {
        _context.Subnets.Add(new Subnet
        {
            Id = 51,
            Name = "app",
            NetworkAddress = "10.51.0.0",
            Cidr = 24,
            CreatedAt = new DateTime(2026, 01, 01, 00, 00, 00, DateTimeKind.Utc),
            CreatedBy = "test-admin"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        byte[] staleToken = [9, 9, 9, 9, 9, 9, 9, 9];
        EditSubnetViewModel viewModel = new()
        {
            Id = 51,
            Name = "appA",
            NetworkAddress = "10.51.0.0",
            Cidr = 24,
            OriginalCidr = 24,
            RowVersion = staleToken
        };

        IActionResult result = await _controller.Edit(51, viewModel);

        ViewResult view = Assert.IsType<ViewResult>(result);
        EditSubnetViewModel shown = Assert.IsType<EditSubnetViewModel>(view.Model);
        Assert.Equal(staleToken, shown.RowVersion);
        Assert.False(_controller.ModelState.TryGetValue(nameof(shown.RowVersion), out _)
            && _controller.ModelState[nameof(shown.RowVersion)]!.Errors.Count > 0);
    }
}
