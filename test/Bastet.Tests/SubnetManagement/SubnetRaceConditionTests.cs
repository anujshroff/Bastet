using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Locking;
using Bastet.Services.Security;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.SubnetManagement;

public class SubnetRaceConditionTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly IUserContextService _userContextService;
    private readonly IIpUtilityService _ipUtilityService;
    private readonly SubnetValidationService _subnetValidationService;
    private readonly HostIpValidationService _hostIpValidationService;
    private readonly ISubnetLockingService _lockingService;
    private readonly IInputSanitizationService _sanitizationService;

    public SubnetRaceConditionTests()
    {

        _context = TestDbContextFactory.CreateDbContext();

        _userContextService = ControllerTestHelper.CreateMockUserContextService();
        _ipUtilityService = new IpUtilityService();
        _subnetValidationService = new SubnetValidationService(_ipUtilityService);
        _hostIpValidationService = new HostIpValidationService(_ipUtilityService, _context);
        _sanitizationService = new InputSanitizationService();

        _lockingService = new SqliteSubnetLockingService();

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
            NetworkAddress = "10.0.1.0",
            Cidr = 24,
            Description = "Test subnet from task 2",
            Tags = "test",
            ParentSubnetId = 1
        };

        SubnetController controller1 = new(_context, _ipUtilityService,
            _subnetValidationService, _hostIpValidationService, _userContextService, _lockingService, NullLogger<SubnetController>.Instance);
        SubnetController controller2 = new(_context, _ipUtilityService,
            _subnetValidationService, _hostIpValidationService, _userContextService, _lockingService, NullLogger<SubnetController>.Instance);

        ControllerTestHelper.SetupController(controller1);
        ControllerTestHelper.SetupController(controller2);

        List<IActionResult> results = [];
        List<Exception> exceptions = [];

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
            }, TestContext.Current.CancellationToken),
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
            }, TestContext.Current.CancellationToken)
        ];

        await Task.WhenAll(tasks).WaitAsync(TestContext.Current.CancellationToken);

        Assert.Empty(exceptions);

        List<Subnet> createdSubnets = await _context.Subnets
            .Where(s => s.ParentSubnetId == 1 && s.Id != 1)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(createdSubnets);

        Assert.Equal(2, results.Count);
        Assert.Single(results.OfType<RedirectToActionResult>());
        Assert.Single(results.OfType<ViewResult>());

        ViewResult viewResult = results.OfType<ViewResult>().First();
        Assert.False(viewResult.ViewData.ModelState.IsValid);
    }

    [Fact]
    public async Task ConcurrentSubnetEdit_WithLocking_PreventsConcurrencyIssues()
    {

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
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        string originalName = subnet.Name;
        string? originalDescription = subnet.Description;
        byte[]? originalRowVersion = subnet.RowVersion;

        EditSubnetViewModel editViewModel1 = new()
        {
            Id = 10,
            Name = "Updated by User 1",
            NetworkAddress = "10.0.5.0",
            Cidr = 24,
            OriginalCidr = 24,
            Description = "Updated by first user",
            RowVersion = originalRowVersion
        };

        EditSubnetViewModel editViewModel2 = new()
        {
            Id = 10,
            Name = "Updated by User 2",
            NetworkAddress = "10.0.5.0",
            Cidr = 24,
            OriginalCidr = 24,
            Description = "Updated by second user",
            RowVersion = originalRowVersion
        };

        SubnetController controller1 = new(_context, _ipUtilityService,
            _subnetValidationService, _hostIpValidationService, _userContextService, _lockingService, NullLogger<SubnetController>.Instance);
        SubnetController controller2 = new(_context, _ipUtilityService,
            _subnetValidationService, _hostIpValidationService, _userContextService, _lockingService, NullLogger<SubnetController>.Instance);

        ControllerTestHelper.SetupController(controller1);
        ControllerTestHelper.SetupController(controller2);

        List<IActionResult> results = [];
        List<Exception> exceptions = [];

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
            }, TestContext.Current.CancellationToken),
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
            }, TestContext.Current.CancellationToken)
        ];

        await Task.WhenAll(tasks).WaitAsync(TestContext.Current.CancellationToken);

        Assert.Empty(exceptions);

        Assert.Equal(2, results.Count);

        Subnet? updatedSubnet = await _context.Subnets.FindAsync([10], TestContext.Current.CancellationToken);
        Assert.NotNull(updatedSubnet);

        List<RedirectToActionResult> redirectResults = [.. results.OfType<RedirectToActionResult>()];
        List<ViewResult> viewResults = [.. results.OfType<ViewResult>()];

        Assert.True(redirectResults.Count + viewResults.Count == 2,
            "Both operations should have returned some result");

        bool wasActuallyUpdated = updatedSubnet.Name != originalName ||
                                 updatedSubnet.Description != originalDescription;

        if (wasActuallyUpdated)
        {

            Assert.True(updatedSubnet.Name is "Updated by User 1" or "Updated by User 2",
                $"Expected subnet name to be one of the edit attempts, but was: {updatedSubnet.Name}");
            Assert.True(updatedSubnet.Description is "Updated by first user" or "Updated by second user",
                $"Expected subnet description to be one of the edit attempts, but was: {updatedSubnet.Description}");

            if (originalRowVersion != null && updatedSubnet.RowVersion != null)
            {
                Assert.NotEqual(originalRowVersion, updatedSubnet.RowVersion);
            }

            bool nameFromUser1 = updatedSubnet.Name == "Updated by User 1";
            bool descFromUser1 = updatedSubnet.Description == "Updated by first user";
            bool nameFromUser2 = updatedSubnet.Name == "Updated by User 2";
            bool descFromUser2 = updatedSubnet.Description == "Updated by second user";

            bool consistentFromUser1 = nameFromUser1 && descFromUser1;
            bool consistentFromUser2 = nameFromUser2 && descFromUser2;

            Assert.True(consistentFromUser1 || consistentFromUser2,
                $"Changes should be consistent from one user. Got Name: '{updatedSubnet.Name}', Description: '{updatedSubnet.Description}'");
        }
        else
        {

            Assert.Equal(originalName, updatedSubnet.Name);
            Assert.Equal(originalDescription, updatedSubnet.Description);

        }
    }

    [Fact]
    public async Task ConcurrentCreateAndBatchImport_WithLocking_CannotCreateOverlappingSiblings()
    {

        CreateSubnetViewModel createViewModel = new()
        {
            Name = "Interactive",
            NetworkAddress = "10.0.9.0",
            Cidr = 24,
            ParentSubnetId = 1
        };

        List<AzureImportSubnetViewModel> batchSubnets =
        [
            new()
            {
                Name = "Imported",
                NetworkAddress = "10.0.9.0",
                Cidr = 25,
                ParentSubnetId = 1
            }
        ];

        SubnetController controller1 = new(_context, _ipUtilityService,
            _subnetValidationService, _hostIpValidationService, _userContextService, _lockingService, NullLogger<SubnetController>.Instance);
        SubnetController controller2 = new(_context, _ipUtilityService,
            _subnetValidationService, _hostIpValidationService, _userContextService, _lockingService, NullLogger<SubnetController>.Instance);

        ControllerTestHelper.SetupController(controller1);
        ControllerTestHelper.SetupController(controller2);

        List<Exception> exceptions = [];

        Task[] tasks =
        [
            Task.Run(async () =>
            {
                try { await controller1.Create(createViewModel); }
                catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
            }, TestContext.Current.CancellationToken),
            Task.Run(async () =>
            {
                try { await controller2.BatchCreateChildSubnets(1, batchSubnets); }
                catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
            }, TestContext.Current.CancellationToken)
        ];

        await Task.WhenAll(tasks).WaitAsync(TestContext.Current.CancellationToken);

        Assert.Empty(exceptions);

        List<Subnet> created = await _context.Subnets
            .Where(s => s.ParentSubnetId == 1 && s.NetworkAddress == "10.0.9.0")
            .ToListAsync(TestContext.Current.CancellationToken);

        Subnet winner = Assert.Single(created);
        Assert.True(winner.Cidr is 24 or 25);
    }
}
