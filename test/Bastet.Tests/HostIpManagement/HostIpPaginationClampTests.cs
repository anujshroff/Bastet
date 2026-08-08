using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.HostIpManagement;

public class HostIpPaginationClampTests : IDisposable
{
    private const int PageSize = 50;

    private readonly BastetDbContext _context;
    private readonly HostIpController _controller;

    public HostIpPaginationClampTests()
    {
        _context = TestDbContextFactory.CreateDbContext();

        IIpUtilityService ipUtility = new IpUtilityService();
        _controller = new HostIpController(
            _context,
            new HostIpValidationService(ipUtility, _context),
            ipUtility,
            ControllerTestHelper.CreateMockUserContextService(),
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<HostIpController>.Instance);
        ControllerTestHelper.SetupController(_controller);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SeedLiveHostIps(int count)
    {
        Subnet subnet = new()
        {
            Name = "subnet-a",
            NetworkAddress = "10.0.0.0",
            Cidr = 24,
            CreatedAt = DateTime.UtcNow
        };
        _context.Subnets.Add(subnet);
        _context.SaveChanges();

        for (int i = 1; i <= count; i++)
        {
            _context.HostIpAssignments.Add(new HostIpAssignment
            {
                IP = $"10.0.0.{i}",
                Name = $"host-{i}",
                SubnetId = subnet.Id,
                CreatedAt = DateTime.UtcNow
            });
        }

        _context.SaveChanges();
    }

    private void SeedArchivedHostIps(int count)
    {
        for (int i = 1; i <= count; i++)
        {
            _context.DeletedHostIpAssignments.Add(new DeletedHostIpAssignment
            {
                OriginalIP = $"10.1.0.{i}",
                Name = $"archived-{i}",
                OriginalSubnetId = 1,
                CreatedAt = DateTime.UtcNow,
                DeletedAt = DateTime.UtcNow,
                DeletedBy = "test-user"
            });
        }

        _context.SaveChanges();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(45_000_000)]
    [InlineData(999_999_999)]
    [InlineData(int.MaxValue)]
    public async Task AllHostIps_PageBeyondTheLastPage_ClampsToTheLastPage(int page)
    {
        SeedLiveHostIps(4);

        ViewResult result = Assert.IsType<ViewResult>(await _controller.AllHostIps(page));
        AllHostIpsViewModel model = Assert.IsType<AllHostIpsViewModel>(result.Model);

        Assert.Equal(1, model.CurrentPage);
        Assert.Equal(4, model.TotalCount);
        Assert.Equal(4, model.HostIps.Count);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(45_000_000)]
    [InlineData(int.MaxValue)]
    public async Task AllDeletedHostIps_PageBeyondTheLastPage_ClampsToTheLastPage(int page)
    {
        SeedArchivedHostIps(40);

        ViewResult result = Assert.IsType<ViewResult>(await _controller.AllDeletedHostIps(page));
        AllDeletedHostIpsViewModel model = Assert.IsType<AllDeletedHostIpsViewModel>(result.Model);

        Assert.Equal(1, model.CurrentPage);
        Assert.Equal(40, model.TotalCount);
        Assert.Equal(40, model.DeletedHostIps.Count);
    }

    [Fact]
    public async Task AllDeletedHostIps_LastRealPage_IsServedIntact()
    {
        SeedArchivedHostIps(61);

        ViewResult result = Assert.IsType<ViewResult>(await _controller.AllDeletedHostIps(2));
        AllDeletedHostIpsViewModel model = Assert.IsType<AllDeletedHostIpsViewModel>(result.Model);

        Assert.Equal(2, model.CurrentPage);
        Assert.Equal(61, model.TotalCount);
        Assert.Equal(11, model.DeletedHostIps.Count);

        ViewResult beyond = Assert.IsType<ViewResult>(await _controller.AllDeletedHostIps(3));
        AllDeletedHostIpsViewModel beyondModel = Assert.IsType<AllDeletedHostIpsViewModel>(beyond.Model);
        Assert.Equal(2, beyondModel.CurrentPage);
        Assert.Equal(11, beyondModel.DeletedHostIps.Count);
    }

    [Fact]
    public async Task AllHostIps_EmptyListing_StaysOnPageOne()
    {
        ViewResult result = Assert.IsType<ViewResult>(await _controller.AllHostIps(45_000_000));
        AllHostIpsViewModel model = Assert.IsType<AllHostIpsViewModel>(result.Model);

        Assert.Equal(1, model.CurrentPage);
        Assert.Equal(0, model.TotalCount);
        Assert.Empty(model.HostIps);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    public async Task AllHostIps_PageBelowOne_FloorsToPageOne(int page)
    {
        SeedLiveHostIps(4);

        ViewResult result = Assert.IsType<ViewResult>(await _controller.AllHostIps(page));
        AllHostIpsViewModel model = Assert.IsType<AllHostIpsViewModel>(result.Model);

        Assert.Equal(1, model.CurrentPage);
        Assert.Equal(4, model.HostIps.Count);
    }

    [Fact]
    public async Task AllHostIps_PageSize_IsFifty()
    {
        SeedLiveHostIps(4);

        ViewResult result = Assert.IsType<ViewResult>(await _controller.AllHostIps(1));
        AllHostIpsViewModel model = Assert.IsType<AllHostIpsViewModel>(result.Model);

        Assert.Equal(PageSize, model.PageSize);
    }
}
