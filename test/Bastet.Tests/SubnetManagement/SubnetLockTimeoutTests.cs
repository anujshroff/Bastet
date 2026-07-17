using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Locking;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.SubnetManagement;

/// <summary>
/// When the global subnet lock cannot be acquired, every guarded action must surface a friendly
/// "try again" response in its own shape (status code, ModelState, TempData) rather than a 500.
/// </summary>
public class SubnetLockTimeoutTests : IDisposable
{
    /// <summary>Simulates lock contention: every acquisition attempt times out.</summary>
    private sealed class AlwaysTimingOutLockService : ISubnetLockingService
    {
        public Task<T> ExecuteWithSubnetLockAsync<T>(Func<Task<T>> operation, TimeSpan? timeout = null) =>
            throw new TimeoutException("Could not acquire subnet operation lock");
    }

    private readonly BastetDbContext _context;
    private readonly IIpUtilityService _ipUtilityService = new IpUtilityService();

    public SubnetLockTimeoutTests()
    {
        _context = TestDbContextFactory.CreateDbContext();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task BatchCreateChildSubnets_LockTimesOut_Returns503()
    {
        SubnetController controller = new(_context, _ipUtilityService,
            new SubnetValidationService(_ipUtilityService), new HostIpValidationService(_ipUtilityService, _context),
            ControllerTestHelper.CreateMockUserContextService(), new AlwaysTimingOutLockService(),
            NullLogger<SubnetController>.Instance);
        ControllerTestHelper.SetupController(controller);

        IActionResult result = await controller.BatchCreateChildSubnets(1,
            [new AzureImportSubnetViewModel { Name = "S", NetworkAddress = "10.0.1.0", Cidr = 24 }]);

        ObjectResult objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, objectResult.StatusCode);
        Assert.Contains("timed out", objectResult.Value?.ToString());
    }

    [Fact]
    public async Task HostIpCreate_LockTimesOut_ReturnsViewWithFriendlyError()
    {
        HostIpController controller = new(_context, new HostIpValidationService(_ipUtilityService, _context),
            _ipUtilityService, ControllerTestHelper.CreateMockUserContextService(),
            new AlwaysTimingOutLockService(), NullLogger<HostIpController>.Instance);
        ControllerTestHelper.SetupController(controller);

        IActionResult result = await controller.Create(new CreateHostIpViewModel
        {
            IP = "10.0.0.5",
            Name = "host",
            SubnetId = 1
        });

        ViewResult view = Assert.IsType<ViewResult>(result);
        Assert.False(view.ViewData.ModelState.IsValid);
        Assert.Contains(view.ViewData.ModelState[""]!.Errors, e => e.ErrorMessage.Contains("timed out"));
    }
}
