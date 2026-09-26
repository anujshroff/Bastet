using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Locking;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.HostIpManagement;

public class HostIpDeleteStaleRowTests : IDisposable
{
    private const string Ip = "10.200.0.9";

    private const string ChangedSinceReview =
        "This host IP changed since you reviewed it. Nothing was deleted. Review its current details and confirm again.";

    private static readonly byte[] Reviewed = [1, 1, 1, 1, 1, 1, 1, 1];
    private static readonly byte[] Changed = [9, 9, 9, 9, 9, 9, 9, 9];

    private readonly BastetDbContext _context;
    private readonly HostIpController _controller;

    public HostIpDeleteStaleRowTests()
    {
        _context = TestDbContextFactory.CreateDbContext();

        _context.Subnets.Add(new Subnet
        {
            Id = 1,
            Name = "hosts",
            NetworkAddress = "10.200.0.0",
            Cidr = 24,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        _context.SaveChanges();

        IIpUtilityService ipUtility = new IpUtilityService();
        _controller = new HostIpController(
            _context,
            new HostIpValidationService(ipUtility, _context),
            ipUtility,
            ControllerTestHelper.CreateMockUserContextService(),
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<HostIpController>.Instance);
        ControllerTestHelper.SetupController(_controller);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task SeedAsync(string name, byte[]? rowVersion)
    {
        _context.HostIpAssignments.Add(new HostIpAssignment
        {
            IP = Ip,
            Name = name,
            SubnetId = 1,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedBy = "test-admin"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        if (rowVersion is not null)
        {
            _ = await _context.Database.ExecuteSqlRawAsync(
                "UPDATE HostIpAssignments SET RowVersion = {0} WHERE IP = {1}", rowVersion, Ip);
        }

        _context.ChangeTracker.Clear();
    }

    private async Task<bool> StillLiveAsync() =>
        await _context.HostIpAssignments.AsNoTracking()
            .AnyAsync(h => h.IP == Ip, TestContext.Current.CancellationToken);

    private async Task<int> ArchivedCountAsync() =>
        await _context.DeletedHostIpAssignments.AsNoTracking()
            .CountAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task TheHarnessCanSeedARowVersion_OrEveryOtherTestHereIsVacuous()
    {
        await SeedAsync("app-01", Reviewed);

        byte[]? stored = await _context.HostIpAssignments.AsNoTracking()
            .Where(h => h.IP == Ip).Select(h => h.RowVersion)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Reviewed, stored);
    }

    [Fact]
    public async Task TheDeletePage_CarriesTheRowVersionOfTheRecordItShows()
    {
        await SeedAsync("app-01", Reviewed);

        ViewResult view = Assert.IsType<ViewResult>(await _controller.Delete(Ip));

        DeleteHostIpViewModel model = Assert.IsType<DeleteHostIpViewModel>(view.Model);
        Assert.Equal("app-01", model.Name);
        Assert.Equal(Reviewed, model.RowVersion);
    }

    [Fact]
    public async Task ConfirmingTheRecordAsReviewed_ArchivesIt()
    {
        await SeedAsync("app-01", Reviewed);

        _ = await _controller.DeleteConfirmed(Ip, "approved", Reviewed);

        Assert.False(await StillLiveAsync());
        Assert.Equal(1, await ArchivedCountAsync());
    }

    [Fact]
    public async Task ConfirmingAfterTheRecordChanged_ArchivesNothing_AndSendsTheOperatorBackToReviewIt()
    {
        await SeedAsync("payments-db", Changed);

        IActionResult result = await _controller.DeleteConfirmed(Ip, "approved", Reviewed);

        Assert.True(await StillLiveAsync());
        Assert.Equal(0, await ArchivedCountAsync());
        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Delete", redirect.ActionName);
        Assert.Equal(Ip, redirect.RouteValues?["ip"]);
        Assert.Equal(ChangedSinceReview, _controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task ConfirmingWithNoRowVersionPosted_ArchivesNothing()
    {
        await SeedAsync("app-01", Reviewed);

        IActionResult result = await _controller.DeleteConfirmed(Ip, "approved", null);

        Assert.True(await StillLiveAsync());
        Assert.Equal(0, await ArchivedCountAsync());
        Assert.Equal("Delete", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal(ChangedSinceReview, _controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task ARecordWithNoRowVersionAtAll_IsNotBlocked()
    {
        await SeedAsync("app-01", null);

        _ = await _controller.DeleteConfirmed(Ip, "approved", null);

        Assert.False(await StillLiveAsync());
    }

    [Fact]
    public async Task TheWrongConfirmationWord_IsStillAnsweredBeforeTheReviewCheck()
    {
        await SeedAsync("payments-db", Changed);

        IActionResult result = await _controller.DeleteConfirmed(Ip, "nope", Reviewed);

        Assert.Equal("Delete", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal("You must type 'approved' to confirm deletion.", _controller.TempData["ErrorMessage"]);
        Assert.True(await StillLiveAsync());
    }

    [Fact]
    public void TheDeleteForm_PostsTheRowVersionOfTheRecordItShows()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Bastet.sln")))
        {
            dir = dir.Parent;
        }

        string form = File.ReadAllText(Path.Combine(
            dir?.FullName ?? throw new InvalidOperationException("Bastet.sln not found above test base directory"),
            "src", "Bastet", "Views", "HostIp", "Delete", "_DeleteConfirmationForm.cshtml"));

        int open = form.IndexOf("<form asp-action=\"Delete\" asp-route-ip=\"@Model.IP\" method=\"post\">", StringComparison.Ordinal);
        int close = form.IndexOf("</form>", StringComparison.Ordinal);
        int input = form.IndexOf(
            "<input type=\"hidden\" name=\"rowVersion\" value=\"@(Model.RowVersion is null ? \"\" : Convert.ToBase64String(Model.RowVersion))\" />",
            StringComparison.Ordinal);
        Assert.True(open >= 0 && close > open, "The host IP delete form element was not found.");
        Assert.True(input > open && input < close, "The rowVersion input must be posted by the delete form itself.");
    }

    private sealed class RowChangedByAWriterHoldingTheLock(BastetDbContext context) : ISubnetLockingService
    {
        public async Task<T> ExecuteWithSubnetLockAsync<T>(Func<Task<T>> operation)
        {
            _ = await context.Database.ExecuteSqlRawAsync(
                "UPDATE HostIpAssignments SET Name = 'payments-db', RowVersion = {0} WHERE IP = {1}", Changed, Ip);
            context.ChangeTracker.Clear();
            return await operation();
        }
    }

    [Fact]
    public async Task TheReviewIsComparedWithTheRowTheLockedDeleteArchives_NotWithAnEarlierRead()
    {
        await SeedAsync("app-01", Reviewed);
        IIpUtilityService ipUtility = new IpUtilityService();
        HostIpController controller = new(
            _context,
            new HostIpValidationService(ipUtility, _context),
            ipUtility,
            ControllerTestHelper.CreateMockUserContextService(),
            new RowChangedByAWriterHoldingTheLock(_context),
            NullLogger<HostIpController>.Instance);
        ControllerTestHelper.SetupController(controller);

        IActionResult result = await controller.DeleteConfirmed(Ip, "approved", Reviewed);

        Assert.True(await StillLiveAsync());
        Assert.Equal(0, await ArchivedCountAsync());
        Assert.Equal("Delete", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal(ChangedSinceReview, controller.TempData["ErrorMessage"]);
    }
}
