using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.SubnetManagement;

public class SubnetDeleteStaleRowTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly SubnetController _controller;

    private static readonly byte[] Reviewed = [1, 1, 1, 1, 1, 1, 1, 1];
    private static readonly byte[] Changed = [9, 9, 9, 9, 9, 9, 9, 9];

    public SubnetDeleteStaleRowTests()
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

    private async Task SeedAsync(byte[] rowVersion)
    {
        _context.Subnets.Add(new Subnet
        {
            Id = 1,
            Name = "DMZ",
            NetworkAddress = "10.77.0.0",
            Cidr = 24,
            CreatedAt = new DateTime(2026, 01, 01, 0, 0, 0, DateTimeKind.Utc),
            CreatedBy = "test-admin",
            RowVersion = rowVersion
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        _ = await _context.Database.ExecuteSqlRawAsync(
            "UPDATE Subnets SET RowVersion = {0} WHERE Id = 1", rowVersion);

        _context.ChangeTracker.Clear();
    }

    private async Task<bool> StillExistsAsync() =>
        await _context.Subnets.AsNoTracking()
            .AnyAsync(s => s.Id == 1, TestContext.Current.CancellationToken);

    [Fact]
    public async Task TheHarnessCanSeedARowVersion_OrEveryOtherTestHereIsVacuous()
    {
        await SeedAsync(Reviewed);
        byte[]? stored = await _context.Subnets.AsNoTracking()
            .Where(s => s.Id == 1).Select(s => s.RowVersion)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(Reviewed, stored);
    }

    [Fact]
    public async Task DeletingWithTheReviewedRowVersion_Succeeds()
    {
        await SeedAsync(Reviewed);

        _ = await _controller.DeleteConfirmed(1, "approved", 0, 0, Reviewed);

        Assert.False(await StillExistsAsync());
    }

    [Fact]
    public async Task DeletingAfterTheRowItselfChanged_IsRefused()
    {
        await SeedAsync(Changed);

        IActionResult result = await _controller.DeleteConfirmed(1, "approved", 0, 0, Reviewed);

        Assert.True(await StillExistsAsync());
        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Delete", redirect.ActionName);
        Assert.Contains("changed since you reviewed it", $"{_controller.TempData["ErrorMessage"]}");
    }

    [Fact]
    public async Task DeletingWithNoRowVersionPosted_IsRefused()
    {
        await SeedAsync(Reviewed);

        IActionResult result = await _controller.DeleteConfirmed(1, "approved", 0, 0, null);

        Assert.True(await StillExistsAsync());
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("changed since you reviewed it", $"{_controller.TempData["ErrorMessage"]}");
    }

    [Fact]
    public async Task ARowWithNoRowVersionAtAll_IsNotBlocked()
    {
        _context.Subnets.Add(new Subnet
        {
            Id = 1,
            Name = "DMZ",
            NetworkAddress = "10.77.0.0",
            Cidr = 24,
            CreatedAt = new DateTime(2026, 01, 01, 0, 0, 0, DateTimeKind.Utc),
            CreatedBy = "test-admin"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        _ = await _controller.DeleteConfirmed(1, "approved", 0, 0, null);

        Assert.False(await StillExistsAsync());
    }
}
