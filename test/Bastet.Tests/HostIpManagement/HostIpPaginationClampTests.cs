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

/// <summary>
/// The two host-IP listings take a page number straight from the query string. It was only floored,
/// never clamped to the number of pages that exist, and the skip was computed in <c>int</c> - so an
/// over-range page rendered an inverted "Showing 51-40 of 40" banner over an empty table, and a page
/// number large enough to overflow the multiplication served page 1's rows while still claiming to be
/// page 45000000.
/// </summary>
/// <remarks>
/// Regression for round 9's I8. Reachable without editing a URL: the app's own pager emits
/// <c>?page=2</c>, and a concurrent purge can shrink the archive under it before the link is followed.
/// </remarks>
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

    /// <summary>Four live host IPs in one /24, so there is exactly one page.</summary>
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
    [InlineData(45_000_000)]        // (page-1)*50 overflows int and Skip treats it as 0
    [InlineData(999_999_999)]
    [InlineData(int.MaxValue)]
    public async Task AllHostIps_PageBeyondTheLastPage_ClampsToTheLastPage(int page)
    {
        SeedLiveHostIps(4);

        ViewResult result = Assert.IsType<ViewResult>(await _controller.AllHostIps(page));
        AllHostIpsViewModel model = Assert.IsType<AllHostIpsViewModel>(result.Model);

        // One page exists, so that is where an out-of-range request lands - and the label, the rows
        // and the pager all derive from CurrentPage, so they cannot disagree.
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

    /// <summary>
    /// The clamp must not disturb a page that really exists, or paging through a large archive breaks.
    /// </summary>
    [Fact]
    public async Task AllDeletedHostIps_LastRealPage_IsServedIntact()
    {
        SeedArchivedHostIps(61);

        ViewResult result = Assert.IsType<ViewResult>(await _controller.AllDeletedHostIps(2));
        AllDeletedHostIpsViewModel model = Assert.IsType<AllDeletedHostIpsViewModel>(result.Model);

        Assert.Equal(2, model.CurrentPage);
        Assert.Equal(61, model.TotalCount);
        Assert.Equal(11, model.DeletedHostIps.Count);

        // Page 3 does not exist, so it clamps back onto page 2 rather than rendering an empty table
        // under a "Showing 101-61 of 61" banner.
        ViewResult beyond = Assert.IsType<ViewResult>(await _controller.AllDeletedHostIps(3));
        AllDeletedHostIpsViewModel beyondModel = Assert.IsType<AllDeletedHostIpsViewModel>(beyond.Model);
        Assert.Equal(2, beyondModel.CurrentPage);
        Assert.Equal(11, beyondModel.DeletedHostIps.Count);
    }

    /// <summary>An empty listing still reports page 1, not page 0.</summary>
    [Fact]
    public async Task AllHostIps_EmptyListing_StaysOnPageOne()
    {
        ViewResult result = Assert.IsType<ViewResult>(await _controller.AllHostIps(45_000_000));
        AllHostIpsViewModel model = Assert.IsType<AllHostIpsViewModel>(result.Model);

        Assert.Equal(1, model.CurrentPage);
        Assert.Equal(0, model.TotalCount);
        Assert.Empty(model.HostIps);
    }

    /// <summary>The existing lower bound still holds: zero and negatives floor to page 1.</summary>
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

    /// <summary>
    /// The page size is what makes the arithmetic above concrete; if it ever changes, these tests
    /// should be revisited rather than silently still passing for the wrong reason.
    /// </summary>
    [Fact]
    public async Task AllHostIps_PageSize_IsFifty()
    {
        SeedLiveHostIps(4);

        ViewResult result = Assert.IsType<ViewResult>(await _controller.AllHostIps(1));
        AllHostIpsViewModel model = Assert.IsType<AllHostIpsViewModel>(result.Model);

        Assert.Equal(PageSize, model.PageSize);
    }
}
