using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.SubnetManagement;

public class SubnetCreateContainerRefusalTests : IDisposable
{
    private readonly BastetDbContext _context = TestDbContextFactory.CreateDbContext();

    public SubnetCreateContainerRefusalTests()
    {
        _context.Subnets.AddRange(
            new Subnet { Id = 1, Name = "root", NetworkAddress = "10.0.0.0", Cidr = 16, CreatedAt = DateTime.UtcNow },
            new Subnet { Id = 2, Name = "hosts", NetworkAddress = "10.0.0.0", Cidr = 24, ParentSubnetId = 1, CreatedAt = DateTime.UtcNow },
            new Subnet { Id = 3, Name = "full", NetworkAddress = "10.0.1.0", Cidr = 24, ParentSubnetId = 1, IsFullyAllocated = true, CreatedAt = DateTime.UtcNow });
        _context.HostIpAssignments.Add(new HostIpAssignment { IP = "10.0.0.10", Name = "h", SubnetId = 2, CreatedAt = DateTime.UtcNow });
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private SubnetController CreateController()
    {
        IIpUtilityService ip = new IpUtilityService();
        SubnetController controller = new(_context, ip, new SubnetValidationService(ip), new HostIpValidationService(ip, _context),
            ControllerTestHelper.CreateMockUserContextService(), ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<SubnetController>.Instance);
        ControllerTestHelper.SetupController(controller);
        return controller;
    }

    [Theory]
    [InlineData("10.0.0.0", null, "hosts (10.0.0.0/24), which has host IP assignments")]
    [InlineData("10.0.0.0", 1, "hosts (10.0.0.0/24), which has host IP assignments")]
    [InlineData("10.0.1.0", null, "full (10.0.1.0/24), which is marked fully allocated")]
    [InlineData("10.0.1.0", 1, "full (10.0.1.0/24), which is marked fully allocated")]
    public async Task Create_InsideAContainerThatCannotHoldChildren_StatesTheFactAndNamesNoRemedy(
        string network, int? parentId, string fact)
    {
        IActionResult result = await CreateController().Create(new CreateSubnetViewModel
        {
            Name = "new",
            NetworkAddress = network,
            Cidr = 26,
            ParentSubnetId = parentId
        });

        ViewResult view = Assert.IsType<ViewResult>(result);
        string[] messages = [.. view.ViewData.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)];

        Assert.Contains(messages, m => m.Contains(fact) && m.EndsWith("so it cannot be created."));
        Assert.DoesNotContain(messages, m => m.Contains("select it instead") || m.Contains("must be a child of"));
        Assert.Empty(_context.Subnets.Where(s => s.Name == "new"));
    }

    [Theory]
    [InlineData(null, "This subnet must be a child of subnet root")]
    [InlineData(4, "A more specific parent subnet exists: root")]
    public async Task Create_InsideAContainerThatCanHoldChildren_StillNamesIt(int? parentId, string remedy)
    {
        _context.Subnets.Add(new Subnet { Id = 4, Name = "top", NetworkAddress = "10.0.0.0", Cidr = 8, CreatedAt = DateTime.UtcNow });
        _context.Subnets.Single(s => s.Id == 1).ParentSubnetId = 4;
        _context.SaveChanges();

        IActionResult result = await CreateController().Create(new CreateSubnetViewModel
        {
            Name = "new",
            NetworkAddress = "10.0.9.0",
            Cidr = 24,
            ParentSubnetId = parentId
        });

        ViewResult view = Assert.IsType<ViewResult>(result);
        string[] messages = [.. view.ViewData.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)];

        Assert.Contains(messages, m => m.Contains(remedy));
    }
}
