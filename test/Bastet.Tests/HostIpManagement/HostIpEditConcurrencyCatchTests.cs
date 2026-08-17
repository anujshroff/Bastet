using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.HostIpManagement;

public class HostIpEditConcurrencyCatchTests : IDisposable
{
    private sealed class SaveThrowingBastetDbContext(DbContextOptions<BastetDbContext> options) : BastetDbContext(options)
    {
        public bool ThrowConcurrencyOnNextSave { get; set; }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            if (ThrowConcurrencyOnNextSave)
            {
                ThrowConcurrencyOnNextSave = false;
                throw new DbUpdateConcurrencyException("simulated conflict between validation and save");
            }

            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }

    private readonly SqliteConnection _connection;
    private readonly SaveThrowingBastetDbContext _context;
    private readonly HostIpController _controller;

    public HostIpEditConcurrencyCatchTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _context = new SaveThrowingBastetDbContext(
            new DbContextOptionsBuilder<BastetDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();

        IIpUtilityService ip = new IpUtilityService();
        _controller = new HostIpController(_context, new HostIpValidationService(ip, _context),
            ip, ControllerTestHelper.CreateMockUserContextService(),
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<HostIpController>.Instance);
        ControllerTestHelper.SetupController(_controller);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Edit_POST_AConflictThatSurfacesAtSave_GetsTheUnifiedReloadAndReapplyMessage()
    {
        _context.Subnets.Add(new Subnet
        {
            Id = 80,
            Name = "office",
            NetworkAddress = "10.80.0.0",
            Cidr = 24,
            CreatedAt = new DateTime(2026, 01, 01, 00, 00, 00, DateTimeKind.Utc),
            CreatedBy = "test-admin"
        });
        _context.HostIpAssignments.Add(new HostIpAssignment
        {
            IP = "10.80.0.5",
            Name = "printer",
            SubnetId = 80,
            CreatedAt = new DateTime(2026, 01, 01, 00, 00, 00, DateTimeKind.Utc),
            CreatedBy = "test-admin"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await _context.Database.ExecuteSqlAsync(
            $"UPDATE HostIpAssignments SET RowVersion = {new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }} WHERE IP = '10.80.0.5'",
            TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        EditHostIpViewModel viewModel = new()
        {
            IP = "10.80.0.5",
            Name = "printer-renamed",
            SubnetId = 80,
            RowVersion = [1, 2, 3, 4, 5, 6, 7, 8]
        };

        _context.ThrowConcurrencyOnNextSave = true;
        IActionResult result = await _controller.Edit("10.80.0.5", viewModel);

        Assert.IsType<ViewResult>(result);
        string message = Assert.Single(
            _controller.ModelState.Values.SelectMany(v => v.Errors),
            e => e.ErrorMessage.Contains("modified by another user")).ErrorMessage;
        Assert.Contains("so it was not saved", message);
        Assert.Contains("Reload the page to see the current values, then re-apply the changes that still make sense", message);
    }
}
