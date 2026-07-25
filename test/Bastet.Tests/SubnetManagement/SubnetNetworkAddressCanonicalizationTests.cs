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
/// Subnet creation must only accept canonical dotted-quad IPv4 network addresses.
/// IPAddress.Parse also accepts partial ("10.0.0"), hex ("0x0A.0.0.0") and zero-padded/octal
/// ("010.0.0.0") forms, which the numeric validation happily aligns while the address is stored
/// and displayed exactly as typed - so the record documents a different network than the one
/// every containment calculation uses.
/// </summary>
public class SubnetNetworkAddressCanonicalizationTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly SubnetController _controller;

    public SubnetNetworkAddressCanonicalizationTests()
    {
        DbContextOptions<BastetDbContext> options = new DbContextOptionsBuilder<BastetDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new BastetDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        IIpUtilityService ipUtilityService = new IpUtilityService();

        _controller = new SubnetController(
            _context,
            ipUtilityService,
            new SubnetValidationService(ipUtilityService),
            new HostIpValidationService(ipUtilityService, _context),
            ControllerTestHelper.CreateMockUserContextService(),
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<SubnetController>.Instance);

        ControllerTestHelper.SetupController(_controller);

        SeedTestData();
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SeedTestData()
    {
        _context.Subnets.AddRange(
            new Subnet
            {
                Id = 1,
                Name = "Root",
                NetworkAddress = "10.0.0.0",
                Cidr = 8,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test-admin"
            },
            new Subnet
            {
                Id = 2,
                Name = "Parent",
                NetworkAddress = "10.0.0.0",
                Cidr = 16,
                ParentSubnetId = 1,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test-admin"
            },
            new Subnet
            {
                Id = 3,
                Name = "Existing Child",
                NetworkAddress = "10.0.0.0",
                Cidr = 24,
                ParentSubnetId = 2,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test-admin"
            });

        _context.SaveChanges();
    }

    /// <summary>
    /// Mirrors a fresh request: the controller must not see entities this test tracked.
    /// </summary>
    private void ResetTracking() => _context.ChangeTracker.Clear();

    [Theory]
    // Zero-padded octets are read as octal: this is 8.0.0.0, not 10.0.0.0.
    [InlineData("010.0.0.0", 8)]
    // Hex octets: this is 11.0.0.0.
    [InlineData("0x0B.0.0.0", 8)]
    // Partial form: the trailing part fills the remaining octets, so this is 12.0.0.0.
    [InlineData("12.0", 8)]
    public async Task Create_WithNonCanonicalTopLevelAddress_IsRejectedAndStoresNothing(string networkAddress, int cidr)
    {
        ResetTracking();
        int subnetCountBefore = await _context.Subnets.CountAsync(TestContext.Current.CancellationToken);

        CreateSubnetViewModel viewModel = new()
        {
            Name = "Alias",
            NetworkAddress = networkAddress,
            Cidr = cidr
        };

        IActionResult result = await _controller.Create(viewModel);

        Assert.IsType<ViewResult>(result);
        Assert.True(_controller.ModelState.ContainsKey("NetworkAddress"));
        Assert.Equal(subnetCountBefore, await _context.Subnets.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_WithNonCanonicalChildAddress_IsRejectedAndStoresNothing()
    {
        ResetTracking();
        int subnetCountBefore = await _context.Subnets.CountAsync(TestContext.Current.CancellationToken);

        // "10.0.010.0" parses to 10.0.8.0 (010 is octal) - inside the /16 parent, correctly aligned
        // for /24, and colliding with nothing, so every numeric check passes and only the stored
        // text is wrong: the subnet reads as 10.0.10.0/24 everywhere it is displayed.
        CreateSubnetViewModel viewModel = new()
        {
            Name = "Alias Child",
            NetworkAddress = "10.0.010.0",
            Cidr = 24,
            ParentSubnetId = 2
        };

        IActionResult result = await _controller.Create(viewModel);

        Assert.IsType<ViewResult>(result);
        Assert.True(_controller.ModelState.ContainsKey("NetworkAddress"));
        Assert.Equal(subnetCountBefore, await _context.Subnets.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_WithAliasOfExistingSubnet_IsRejectedForTheAddressItself()
    {
        ResetTracking();
        int subnetCountBefore = await _context.Subnets.CountAsync(TestContext.Current.CancellationToken);

        // "10.0.0" is 10.0.0.0 - the same network as the seeded /24. The duplicate lookup and the
        // parent self-skip both compare NetworkAddress as text, so neither recognises it.
        CreateSubnetViewModel viewModel = new()
        {
            Name = "Alias Duplicate",
            NetworkAddress = "10.0.0",
            Cidr = 24,
            ParentSubnetId = 2
        };

        IActionResult result = await _controller.Create(viewModel);

        Assert.IsType<ViewResult>(result);
        Assert.True(_controller.ModelState.ContainsKey("NetworkAddress"));
        Assert.Equal(subnetCountBefore, await _context.Subnets.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_WithCanonicalAddress_StillSucceeds()
    {
        ResetTracking();

        CreateSubnetViewModel viewModel = new()
        {
            Name = "Canonical Child",
            NetworkAddress = "10.1.0.0",
            Cidr = 16,
            ParentSubnetId = 1
        };

        IActionResult result = await _controller.Create(viewModel);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(await _context.Subnets.AnyAsync(s => s.NetworkAddress == "10.1.0.0" && s.Cidr == 16, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The Azure import paths build their view models in code, so the model-binding attributes never
    /// run for them - the guard has to live in the shared creation validation.
    /// </summary>
    [Fact]
    public async Task BatchCreateChildSubnets_WithNonCanonicalAddress_IsRejectedAndStoresNothing()
    {
        ResetTracking();
        int subnetCountBefore = await _context.Subnets.CountAsync(TestContext.Current.CancellationToken);

        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {
                // Hex octet: parses to 10.0.11.0, aligned and inside the parent.
                Name = "Imported Alias",
                NetworkAddress = "10.0.0x0B.0",
                Cidr = 24,
                ParentSubnetId = 2
            }
        ];

        IActionResult result = await _controller.BatchCreateChildSubnets(2, subnets);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(subnetCountBefore, await _context.Subnets.CountAsync(TestContext.Current.CancellationToken));
    }
}

