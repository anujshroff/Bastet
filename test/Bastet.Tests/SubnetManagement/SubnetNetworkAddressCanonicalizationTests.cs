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

    private void ResetTracking() => _context.ChangeTracker.Clear();

    [Theory]

    [InlineData("010.0.0.0", 8)]

    [InlineData("0x0B.0.0.0", 8)]

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

    [Fact]
    public async Task BatchCreateChildSubnets_WithNonCanonicalAddress_IsRejectedAndStoresNothing()
    {
        ResetTracking();
        int subnetCountBefore = await _context.Subnets.CountAsync(TestContext.Current.CancellationToken);

        List<AzureImportSubnetViewModel> subnets =
        [
            new()
            {

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
