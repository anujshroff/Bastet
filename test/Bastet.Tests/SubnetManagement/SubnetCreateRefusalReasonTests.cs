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

public class SubnetCreateRefusalReasonTests : IDisposable
{
    private const string ContainmentSentence = "Child subnet must be contained within the parent subnet range";
    private const string CidrSentence = "Child subnet CIDR must be larger than parent subnet CIDR";

    private readonly BastetDbContext _context;
    private readonly SubnetController _controller;

    public SubnetCreateRefusalReasonTests()
    {
        _context = TestDbContextFactory.CreateDbContext();

        _context.Subnets.Add(new Subnet
        {
            Id = 1,
            Name = "parent",
            NetworkAddress = "10.50.0.0",
            Cidr = 24,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        _context.SaveChanges();

        IIpUtilityService ipUtility = new IpUtilityService();
        _controller = new SubnetController(
            _context,
            ipUtility,
            new SubnetValidationService(ipUtility),
            new HostIpValidationService(ipUtility, _context),
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

    private async Task<Dictionary<string, string[]>> RefusalAsync(string networkAddress, int cidr)
    {
        IActionResult result = await _controller.Create(new CreateSubnetViewModel
        {
            Name = "child",
            NetworkAddress = networkAddress,
            Cidr = cidr,
            ParentSubnetId = 1
        });

        _ = Assert.IsType<ViewResult>(result);

        return _controller.ModelState
            .Where(entry => entry.Value is not null && entry.Value.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray());
    }

    [Theory]
    [InlineData("10.50.0.0", 24)]
    [InlineData("10.50.0.0", 16)]
    public async Task ARangeEqualToOrLargerThanTheParent_IsRefusedOnCidr_NotOnContainment(
        string networkAddress, int cidr)
    {
        Dictionary<string, string[]> errors = await RefusalAsync(networkAddress, cidr);

        Assert.True(errors.ContainsKey(nameof(CreateSubnetViewModel.Cidr)),
            "the refusal must be keyed to the field that has to change");
        Assert.Contains(CidrSentence, string.Join(" ", errors[nameof(CreateSubnetViewModel.Cidr)]));

        Assert.DoesNotContain(ContainmentSentence, string.Join(" ", errors.Values.SelectMany(v => v)));
    }

    [Theory]
    [InlineData("10.60.0.0", 25)]
    [InlineData("10.50.1.0", 25)]
    public async Task ARangeOutsideTheParent_IsStillRefusedOnContainment(string networkAddress, int cidr)
    {
        Dictionary<string, string[]> errors = await RefusalAsync(networkAddress, cidr);

        Assert.True(errors.ContainsKey(nameof(CreateSubnetViewModel.NetworkAddress)));
        Assert.Contains(ContainmentSentence,
            string.Join(" ", errors[nameof(CreateSubnetViewModel.NetworkAddress)]));
    }

    [Fact]
    public async Task AValidChild_IsStillAccepted()
    {
        IActionResult result = await _controller.Create(new CreateSubnetViewModel
        {
            Name = "child",
            NetworkAddress = "10.50.0.0",
            Cidr = 26,
            ParentSubnetId = 1
        });

        _ = Assert.IsType<RedirectToActionResult>(result);
    }
}
