using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data.Common;

namespace Bastet.Tests.SubnetManagement;

internal sealed class ChildCommittedBeforeCommand(int id, string network, int cidr, int parentId)
    : DbCommandInterceptor
{
    private bool _armed;
    private int _beforeCommand;
    private int _commandsSeen;

    public bool Inserted { get; private set; }

    public void Arm(int beforeCommand)
    {
        _armed = true;
        _beforeCommand = beforeCommand;
        _commandsSeen = 0;
        Inserted = false;
    }

    public void Disarm() => _armed = false;

    private void BeforeCommand(DbCommand command)
    {
        if (!_armed)
        {
            return;
        }

        _commandsSeen++;
        if (_commandsSeen == _beforeCommand && !Inserted)
        {
            using DbCommand insert = command.Connection!.CreateCommand();
            insert.Transaction = command.Transaction;
            insert.CommandText =
                "INSERT INTO Subnets (Id, Name, NetworkAddress, Cidr, ParentSubnetId, IsFullyAllocated, CreatedAt) "
                + $"VALUES ({id}, 'created-while-the-page-loaded', '{network}', {cidr}, {parentId}, 0, '2026-01-01 00:00:00')";
            _ = insert.ExecuteNonQuery();
            Inserted = true;
        }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        BeforeCommand(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        BeforeCommand(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        BeforeCommand(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        BeforeCommand(command);
        return ValueTask.FromResult(result);
    }
}

public class SubnetDeletePageSnapshotTests : IDisposable
{
    private const int LateChildId = 60006;

    private readonly ChildCommittedBeforeCommand _race;
    private readonly BastetDbContext _context;
    private readonly SubnetController _controller;

    public SubnetDeletePageSnapshotTests()
    {
        _race = new ChildCommittedBeforeCommand(LateChildId, "10.210.2.0", 24, parentId: 1);

        DbContextOptions<BastetDbContext> options = new DbContextOptionsBuilder<BastetDbContext>()
            .UseSqlite("DataSource=:memory:")
            .AddInterceptors(_race)
            .Options;

        _context = new BastetDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

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
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Seed(bool withReviewedChild)
    {
        _context.Subnets.Add(new Subnet
        {
            Id = 1,
            Name = "dr-parent",
            NetworkAddress = "10.210.0.0",
            Cidr = 16,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        if (withReviewedChild)
        {
            _context.Subnets.Add(new Subnet
            {
                Id = 2,
                Name = "dr-child",
                NetworkAddress = "10.210.1.0",
                Cidr = 24,
                ParentSubnetId = 1,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        }
        _context.SaveChanges();
        _context.ChangeTracker.Clear();
    }

    private async Task<DeleteSubnetViewModel> LoadTheDeletePageWhileAChildIsCreatedAsync(int beforeCommand)
    {
        _race.Arm(beforeCommand);
        IActionResult page = await _controller.Delete(1);
        _race.Disarm();
        Assert.True(_race.Inserted, "The child was never created during the page load, so this test measures nothing.");
        _context.ChangeTracker.Clear();
        return Assert.IsType<DeleteSubnetViewModel>(Assert.IsType<ViewResult>(page).Model);
    }

    private async Task<bool> LateChildStillLiveAsync() =>
        await _context.Subnets.AsNoTracking()
            .AnyAsync(s => s.Id == LateChildId, TestContext.Current.CancellationToken);

    private async Task<int> DescendantsWithinTheBoundAsync(int bound) =>
        await _context.Subnets.AsNoTracking()
            .CountAsync(s => s.Id != 1 && s.Id <= bound, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(2, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(4, false)]
    public async Task TheDeletePage_ShowsExactlyTheSubtreeItPostsAsTheScope_WhenAChildIsCreatedWhileItLoads(int beforeCommand, bool withReviewedChild)
    {
        Seed(withReviewedChild);

        DeleteSubnetViewModel page = await LoadTheDeletePageWhileAChildIsCreatedAsync(beforeCommand);

        Assert.Equal(await DescendantsWithinTheBoundAsync(page.ConfirmedMaxSubnetId), page.ChildSubnetCount);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(4, false)]
    public async Task ConfirmingThatPage_ArchivesExactlyTheChildrenItShowed_OrNothing(int beforeCommand, bool withReviewedChild)
    {
        Seed(withReviewedChild);
        DeleteSubnetViewModel page = await LoadTheDeletePageWhileAChildIsCreatedAsync(beforeCommand);

        IActionResult result = await _controller.DeleteConfirmed(
            1, "approved", page.ConfirmedMaxSubnetId, page.HostIpCount, page.RowVersion);

        List<int> archived = await _context.DeletedSubnets.AsNoTracking()
            .Select(d => d.OriginalId).ToListAsync(TestContext.Current.CancellationToken);
        if (archived.Count > 0)
        {
            Assert.Contains(1, archived);
            Assert.Equal(page.ChildSubnetCount, archived.Count - 1);
        }
        else
        {
            Assert.True(await LateChildStillLiveAsync());
            Assert.Equal("Delete", Assert.IsType<RedirectToActionResult>(result).ActionName);
            Assert.Contains("added beneath this subnet after you reviewed it", $"{_controller.TempData["ErrorMessage"]}");
        }
    }

    [Fact]
    public async Task WithoutARace_ThePageShowsAndBindsEveryDescendant()
    {
        Seed(withReviewedChild: true);

        IActionResult page = await _controller.Delete(1);

        DeleteSubnetViewModel model = Assert.IsType<DeleteSubnetViewModel>(Assert.IsType<ViewResult>(page).Model);
        Assert.Equal(1, model.ChildSubnetCount);
        Assert.Equal(2, model.ConfirmedMaxSubnetId);

        _ = await _controller.DeleteConfirmed(1, "approved", model.ConfirmedMaxSubnetId, model.HostIpCount, model.RowVersion);

        Assert.Equal(2, await _context.DeletedSubnets.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WithoutARace_ADeepTreeWhoseNewestRowIsAGrandchild_IsShownBoundAndDeletedWhole()
    {
        Seed(withReviewedChild: true);
        _context.Subnets.AddRange(
            new Subnet
            {
                Id = 3,
                Name = "dr-child-b",
                NetworkAddress = "10.210.8.0",
                Cidr = 24,
                ParentSubnetId = 1,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Subnet
            {
                Id = 9,
                Name = "dr-grandchild",
                NetworkAddress = "10.210.1.0",
                Cidr = 26,
                ParentSubnetId = 2,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        IActionResult page = await _controller.Delete(1);

        DeleteSubnetViewModel model = Assert.IsType<DeleteSubnetViewModel>(Assert.IsType<ViewResult>(page).Model);
        Assert.Equal(3, model.ChildSubnetCount);
        Assert.Equal(9, model.ConfirmedMaxSubnetId);

        _ = await _controller.DeleteConfirmed(1, "approved", model.ConfirmedMaxSubnetId, model.HostIpCount, model.RowVersion);

        Assert.Equal(4, await _context.DeletedSubnets.CountAsync(TestContext.Current.CancellationToken));
        Assert.False(await _context.Subnets.AnyAsync(TestContext.Current.CancellationToken));
    }
}
