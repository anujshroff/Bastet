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

namespace Bastet.Tests.SubnetManagement;

/// <summary>
/// Tests to verify that race conditions are properly prevented in subnet operations
/// </summary>
public class SubnetRaceConditionTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly IUserContextService _userContextService;
    private readonly IIpUtilityService _ipUtilityService;
    private readonly SubnetValidationService _subnetValidationService;
    private readonly HostIpValidationService _hostIpValidationService;
    private readonly ISubnetLockingService _lockingService;

    public SubnetRaceConditionTests()
    {
        // Create in-memory database context
        _context = TestDbContextFactory.CreateDbContext();

        // Create services
        _userContextService = ControllerTestHelper.CreateMockUserContextService();
        _ipUtilityService = new IpUtilityService();
        _subnetValidationService = new SubnetValidationService(_ipUtilityService);
        _hostIpValidationService = new HostIpValidationService(_ipUtilityService, _context);

        // Use the real SQLite locking service for these tests
        _lockingService = new SqliteSubnetLockingService(_context);

        // Set up test data
        SeedTestData();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SeedTestData()
    {
        // Create a parent subnet for testing
        Subnet parentSubnet = new()
        {
            Id = 1,
            Name = "Parent Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 16,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.Subnets.Add(parentSubnet);
        _context.SaveChanges();
    }

    [Fact]
    public async Task ConcurrentSubnetCreation_WithLocking_PreventsDuplicates()
    {
        // Arrange - Create two identical subnet creation requests
        CreateSubnetViewModel createViewModel1 = new()
        {
            Name = "Test Subnet 1",
            NetworkAddress = "10.0.1.0",
            Cidr = 24,
            Description = "Test subnet from task 1",
            Tags = "test",
            ParentSubnetId = 1
        };

        CreateSubnetViewModel createViewModel2 = new()
        {
            Name = "Test Subnet 2",
            NetworkAddress = "10.0.1.0", // Same network address - should conflict
            Cidr = 24,
            Description = "Test subnet from task 2",
            Tags = "test",
            ParentSubnetId = 1
        };

        // Create two controllers with real locking service
        SubnetController controller1 = new(_context, _ipUtilityService,
            _subnetValidationService, _hostIpValidationService, _userContextService, _lockingService);
        SubnetController controller2 = new(_context, _ipUtilityService,
            _subnetValidationService, _hostIpValidationService, _userContextService, _lockingService);

        ControllerTestHelper.SetupController(controller1);
        ControllerTestHelper.SetupController(controller2);

        // Track results
        List<IActionResult> results = [];
        List<Exception> exceptions = [];

        // Act - Execute both creations concurrently
        Task[] tasks =
        [
            Task.Run(async () =>
            {
                try
                {
                    IActionResult result = await controller1.Create(createViewModel1);
                    lock (results) { results.Add(result); }
                }
                catch (Exception ex)
                {
                    lock (exceptions) { exceptions.Add(ex); }
                }
            }),
            Task.Run(async () =>
            {
                try
                {
                    IActionResult result = await controller2.Create(createViewModel2);
                    lock (results) { results.Add(result); }
                }
                catch (Exception ex)
                {
                    lock (exceptions) { exceptions.Add(ex); }
                }
            })
        ];

        await Task.WhenAll(tasks);

        // Assert - Only one subnet should be created, one should fail
        Assert.Empty(exceptions); // No unhandled exceptions should occur

        // Check database state
        List<Subnet> createdSubnets = await _context.Subnets
            .Where(s => s.ParentSubnetId == 1 && s.Id != 1)
            .ToListAsync();

        // With proper locking, only one subnet should be created
        Assert.Single(createdSubnets);

        // One request should succeed (redirect), one should fail (view with validation error)
        Assert.Equal(2, results.Count);
        Assert.Single(results.OfType<RedirectToActionResult>());
        Assert.Single(results.OfType<ViewResult>());

        // The failed request should have validation errors
        ViewResult viewResult = results.OfType<ViewResult>().First();
        Assert.False(viewResult.ViewData.ModelState.IsValid);
    }

    [Fact]
    public async Task ConcurrentSubnetEdit_WithLocking_PreventsConcurrencyIssues()
    {
        // Arrange - Create a subnet to edit
        Subnet subnet = new()
        {
            Id = 10,
            Name = "Edit Test Subnet",
            NetworkAddress = "10.0.5.0",
            Cidr = 24,
            ParentSubnetId = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.Subnets.Add(subnet);
        await _context.SaveChangesAsync();

        // Create two edit requests that modify different properties
        EditSubnetViewModel editViewModel1 = new()
        {
            Id = 10,
            Name = "Updated by User 1",
            NetworkAddress = "10.0.5.0",
            Cidr = 24,
            OriginalCidr = 24,
            Description = "Updated by first user",
            RowVersion = subnet.RowVersion
        };

        EditSubnetViewModel editViewModel2 = new()
        {
            Id = 10,
            Name = "Updated by User 2",
            NetworkAddress = "10.0.5.0",
            Cidr = 24,
            OriginalCidr = 24,
            Description = "Updated by second user",
            RowVersion = subnet.RowVersion // Same row version - will cause concurrency conflict
        };

        // Create two controllers with real locking service
        SubnetController controller1 = new(_context, _ipUtilityService,
            _subnetValidationService, _hostIpValidationService, _userContextService, _lockingService);
        SubnetController controller2 = new(_context, _ipUtilityService,
            _subnetValidationService, _hostIpValidationService, _userContextService, _lockingService);

        ControllerTestHelper.SetupController(controller1);
        ControllerTestHelper.SetupController(controller2);

        // Track results
        List<IActionResult> results = [];
        List<Exception> exceptions = [];

        // Act - Execute both edits concurrently
        Task[] tasks =
        [
            Task.Run(async () =>
            {
                try
                {
                    IActionResult result = await controller1.Edit(10, editViewModel1);
                    lock (results) { results.Add(result); }
                }
                catch (Exception ex)
                {
                    lock (exceptions) { exceptions.Add(ex); }
                }
            }),
            Task.Run(async () =>
            {
                try
                {
                    IActionResult result = await controller2.Edit(10, editViewModel2);
                    lock (results) { results.Add(result); }
                }
                catch (Exception ex)
                {
                    lock (exceptions) { exceptions.Add(ex); }
                }
            })
        ];

        await Task.WhenAll(tasks);

        // Assert - No unhandled exceptions should occur
        Assert.Empty(exceptions);

        // Check results
        Assert.Equal(2, results.Count);

        // One should succeed (redirect), one should handle concurrency gracefully (view)
        List<RedirectToActionResult> redirectResults = [.. results.OfType<RedirectToActionResult>()];
        List<ViewResult> viewResults = [.. results.OfType<ViewResult>()];

        // With proper locking and concurrency handling, we should have:
        // - One successful update (redirect)
        // - One concurrency conflict handled gracefully (view with error)
        Assert.Single(redirectResults);
        Assert.Single(viewResults);

        // Verify the subnet was updated exactly once
        Subnet? updatedSubnet = await _context.Subnets.FindAsync(10);
        Assert.NotNull(updatedSubnet);

        // The successful update should have persisted
        Assert.True(updatedSubnet.Name is "Updated by User 1" or "Updated by User 2");
        Assert.True(updatedSubnet.Description is "Updated by first user" or "Updated by second user");
    }
}
