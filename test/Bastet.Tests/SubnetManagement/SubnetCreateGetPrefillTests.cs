using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Security;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.ComponentModel.DataAnnotations;

namespace Bastet.Tests.SubnetManagement;

public class SubnetCreateGetPrefillTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly SubnetController _controller;

    public SubnetCreateGetPrefillTests()
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

        _context.Subnets.Add(new Subnet
        {
            Id = 1,
            Name = "Parent",
            NetworkAddress = "10.0.0.0",
            Cidr = 16,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        });
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private static CreateSubnetViewModel ModelOf(IActionResult result) =>
        Assert.IsType<CreateSubnetViewModel>(Assert.IsType<ViewResult>(result).Model);

    [Theory]
    [InlineData(33)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public async Task Create_CidrOutsideValidRange_RendersTheFormInsteadOfThrowing(int cidr)
    {
        IActionResult result = await _controller.Create(networkAddress: "10.0.0.0", cidr: cidr);

        CreateSubnetViewModel model = ModelOf(result);
        Assert.Empty(model.CalculatedSubnetMask);
        Assert.Equal(0, model.Cidr);
        Assert.Equal("10.0.0.0", model.NetworkAddress);
    }

    [Fact]
    public async Task Create_CidrOutsideValidRange_DoesNotComposeAName()
    {
        IActionResult result = await _controller.Create(networkAddress: "10.0.0.0", cidr: 33, parentId: 1);

        Assert.Empty(ModelOf(result).Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    [InlineData(32)]
    public async Task Create_CidrInsideValidRange_IsStillPreFilled(int cidr)
    {
        IActionResult result = await _controller.Create(networkAddress: "10.0.0.0", cidr: cidr);

        CreateSubnetViewModel model = ModelOf(result);
        Assert.Equal(cidr, model.Cidr);
        Assert.NotEmpty(model.CalculatedSubnetMask);
    }

    [Theory]
    [InlineData("10.0.1.0", 24, 1)]
    [InlineData("10.0.9.9", 32, 1)]
    public async Task Create_PrefilledName_PassesTheValidationThePostApplies(
        string networkAddress, int cidr, int parentId)
    {
        IActionResult result = await _controller.Create(networkAddress, cidr, parentId);
        string name = ModelOf(result).Name;

        SafeTextAttribute rule = new();
        ValidationContext context = new(new object(), new SafeTextServiceProvider(), null);

        Assert.True(
            rule.GetValidationResult(name, context) == System.ComponentModel.DataAnnotations.ValidationResult.Success,
            $"the prefilled name '{name}' is refused by the rule its own POST applies");
    }

    [Theory]
    [InlineData("Prod/Web", "ProdWeb-10.7.1.0-24")]
    [InlineData("Bob's Lab", "Bobs Lab-10.7.1.0-24")]
    [InlineData("DC1:Core", "DC1Core-10.7.1.0-24")]
    [InlineData("/ / /", "10.7.1.0-24")]
    public async Task Create_ParentNameOutsideSafeText_PrefillStillPassesThePost(
        string parentName, string expectedName)
    {
        _context.Subnets.Add(new Subnet
        {
            Id = 7,
            Name = parentName,
            NetworkAddress = "10.7.0.0",
            Cidr = 16,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IActionResult result = await _controller.Create(networkAddress: "10.7.1.0", cidr: 24, parentId: 7);
        string name = ModelOf(result).Name;

        Assert.Equal(expectedName, name);

        SafeTextAttribute rule = new();
        ValidationContext context = new(new object(), new SafeTextServiceProvider(), null);
        Assert.True(
            rule.GetValidationResult(name, context) == System.ComponentModel.DataAnnotations.ValidationResult.Success,
            $"the prefilled name '{name}' is refused by the rule its own POST applies");
    }

    private sealed class SafeTextServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IInputSanitizationService) ? new InputSanitizationService() : null;
    }

    [Fact]
    public async Task Create_ValidCidrWithParent_ComposesTheDefaultName()
    {
        IActionResult result = await _controller.Create(networkAddress: "10.0.1.0", cidr: 24, parentId: 1);

        Assert.Equal("Parent-10.0.1.0-24", ModelOf(result).Name);
    }

    [Fact]
    public async Task Create_LongParentName_ComposesANameThatFitsTheLimit()
    {
        _context.Subnets.Add(new Subnet
        {
            Id = 2,
            Name = new string('p', 95),
            NetworkAddress = "10.5.0.0",
            Cidr = 16,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IActionResult result = await _controller.Create(networkAddress: "10.5.1.0", cidr: 24, parentId: 2);

        string name = ModelOf(result).Name;
        Assert.True(name.Length <= 100, $"generated name was {name.Length} characters");
        Assert.EndsWith("-10.5.1.0-24", name);
    }
}
