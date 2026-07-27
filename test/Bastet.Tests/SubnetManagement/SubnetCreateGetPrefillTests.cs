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
/// The Create GET action pre-fills its form from query-string values. Those arrive straight off the
/// URL and reach the view model without ever passing its [Range]/[StringLength] attributes, which
/// only run on the POST - so every one of them has to be treated as advice rather than instruction.
/// </summary>
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

    /// <summary>
    /// CalculateSubnetMask throws outside 0-32, and every other caller reads the CIDR from the
    /// database where CK_Subnet_ValidCidr constrains it. This action is the only entry point that
    /// takes one from the outside, so an out-of-range value returned a 500 rather than a blank form.
    /// </summary>
    [Theory]
    [InlineData(33)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public async Task Create_CidrOutsideValidRange_RendersTheFormInsteadOfThrowing(int cidr)
    {
        IActionResult result = await _controller.Create(networkAddress: "10.0.0.0", cidr: cidr);

        CreateSubnetViewModel model = ModelOf(result);
        Assert.Empty(model.CalculatedSubnetMask);         // left blank, not computed
        Assert.Equal(0, model.Cidr);                      // the bad value was not carried through
        Assert.Equal("10.0.0.0", model.NetworkAddress);   // the usable half is still pre-filled
    }

    /// <summary>An out-of-range CIDR must not reach the generated name either.</summary>
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

    [Fact]
    public async Task Create_ValidCidrWithParent_ComposesTheDefaultName()
    {
        IActionResult result = await _controller.Create(networkAddress: "10.0.1.0", cidr: 24, parentId: 1);

        Assert.Equal("Parent-10.0.1.0/24", ModelOf(result).Name);
    }

    /// <summary>
    /// The suffix runs to 13 characters, so a long parent name used to compose a value the very next
    /// POST rejected against [StringLength(100)]. The parent name is what gives way - the address
    /// and CIDR are the part that makes the generated name mean anything.
    /// </summary>
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
        Assert.EndsWith("-10.5.1.0/24", name);   // the suffix survives intact
    }
}
