using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.DTOs;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Security;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.HostIpManagement;

public class SubnetHostIpInteractionTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly IUserContextService _userContextService;
    private readonly IIpUtilityService _ipUtilityService;
    private readonly SubnetValidationService _subnetValidationService;
    private readonly HostIpValidationService _hostIpValidationService;
    private readonly IInputSanitizationService _sanitizationService;
    private readonly SubnetController _subnetController;
    private readonly HostIpController _hostIpController;

    public SubnetHostIpInteractionTests()
    {

        _context = TestDbContextFactory.CreateDbContext();

        _userContextService = ControllerTestHelper.CreateMockUserContextService();
        _ipUtilityService = new IpUtilityService();
        _subnetValidationService = new SubnetValidationService(_ipUtilityService);
        _hostIpValidationService = new HostIpValidationService(_ipUtilityService, _context);
        _sanitizationService = new InputSanitizationService();

        _subnetController = new SubnetController(_context, _ipUtilityService,
            _subnetValidationService, _hostIpValidationService, _userContextService,
            ControllerTestHelper.CreateMockSubnetLockingService(), NullLogger<SubnetController>.Instance);
        ControllerTestHelper.SetupController(_subnetController);

        _hostIpController = new HostIpController(_context, _hostIpValidationService,
            _ipUtilityService, _userContextService, ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<HostIpController>.Instance);
        ControllerTestHelper.SetupController(_hostIpController);

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

        Subnet expandableSubnet = new()
        {
            Id = 1,
            Name = "Expandable Subnet",
            NetworkAddress = "192.168.0.0",
            Cidr = 24,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.Subnets.Add(expandableSubnet);

        HostIpAssignment hostIp1 = new()
        {
            IP = "192.168.0.10",
            Name = "Host 1",
            SubnetId = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.HostIpAssignments.Add(hostIp1);

        HostIpAssignment hostIp2 = new()
        {
            IP = "192.168.0.20",
            Name = "Host 2",
            SubnetId = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.HostIpAssignments.Add(hostIp2);

        Subnet shrinkableSubnet = new()
        {
            Id = 2,
            Name = "Shrinkable Subnet",
            NetworkAddress = "172.16.0.0",
            Cidr = 23,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.Subnets.Add(shrinkableSubnet);

        HostIpAssignment hostIp3 = new()
        {
            IP = "172.16.0.10",
            Name = "Host 3",
            SubnetId = 2,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.HostIpAssignments.Add(hostIp3);

        HostIpAssignment hostIp4 = new()
        {
            IP = "172.16.0.20",
            Name = "Host 4",
            SubnetId = 2,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.HostIpAssignments.Add(hostIp4);

        Subnet unshrinkableSubnet = new()
        {
            Id = 3,
            Name = "Unshrinkable Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 23,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.Subnets.Add(unshrinkableSubnet);

        HostIpAssignment hostIp5 = new()
        {
            IP = "10.0.0.10",
            Name = "Host 5 - First Half",
            SubnetId = 3,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.HostIpAssignments.Add(hostIp5);

        HostIpAssignment hostIp6 = new()
        {
            IP = "10.0.1.10",
            Name = "Host 6 - Second Half",
            SubnetId = 3,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.HostIpAssignments.Add(hostIp6);

        Subnet parentSubnet = new()
        {
            Id = 4,
            Name = "Parent Subnet",
            NetworkAddress = "10.10.0.0",
            Cidr = 16,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.Subnets.Add(parentSubnet);

        Subnet childSubnet1 = new()
        {
            Id = 5,
            Name = "Child Subnet 1",
            NetworkAddress = "10.10.1.0",
            Cidr = 24,
            ParentSubnetId = 4,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.Subnets.Add(childSubnet1);

        HostIpAssignment hostIp7 = new()
        {
            IP = "10.10.1.10",
            Name = "Host 7",
            SubnetId = 5,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.HostIpAssignments.Add(hostIp7);

        Subnet childSubnet2 = new()
        {
            Id = 6,
            Name = "Child Subnet 2",
            NetworkAddress = "10.10.2.0",
            Cidr = 24,
            ParentSubnetId = 4,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.Subnets.Add(childSubnet2);

        Subnet emptySubnet = new()
        {
            Id = 7,
            Name = "Empty Subnet",
            NetworkAddress = "10.20.0.0",
            Cidr = 24,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.Subnets.Add(emptySubnet);

        _context.SaveChanges();
    }

    [Fact]
    public async Task EditSubnet_DecreaseCidr_WithHostIps_Succeeds()
    {

        int subnetId = 1;
        EditSubnetViewModel viewModel = new()
        {
            Id = subnetId,
            Name = "Expandable Subnet",
            NetworkAddress = "192.168.0.0",
            Cidr = 23,
            OriginalCidr = 24,
            Description = "Expanded subnet"
        };

        IActionResult result = await _subnetController.Edit(subnetId, viewModel);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(subnetId, redirectResult.RouteValues?["id"]);

        Subnet? updatedSubnet = await _context.Subnets.FindAsync([subnetId], TestContext.Current.CancellationToken);
        Assert.NotNull(updatedSubnet);
        Assert.Equal(23, updatedSubnet.Cidr);
    }

    [Fact]
    public async Task EditSubnet_IncreaseCidr_AllHostIpsStillInRange_Succeeds()
    {

        int subnetId = 2;
        EditSubnetViewModel viewModel = new()
        {
            Id = subnetId,
            Name = "Shrinkable Subnet",
            NetworkAddress = "172.16.0.0",
            Cidr = 24,
            OriginalCidr = 23,
            Description = "Shrunk subnet"
        };

        IActionResult result = await _subnetController.Edit(subnetId, viewModel);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(subnetId, redirectResult.RouteValues?["id"]);

        Subnet? updatedSubnet = await _context.Subnets.FindAsync([subnetId], TestContext.Current.CancellationToken);
        Assert.NotNull(updatedSubnet);
        Assert.Equal(24, updatedSubnet.Cidr);
    }

    [Fact]
    public async Task EditSubnet_IncreaseCidr_HostIpOutOfRange_Fails()
    {

        int subnetId = 3;
        EditSubnetViewModel viewModel = new()
        {
            Id = subnetId,
            Name = "Unshrinkable Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 24,
            OriginalCidr = 23,
            Description = "Attempted to shrink"
        };

        IActionResult result = await _subnetController.Edit(subnetId, viewModel);

        _ = Assert.IsType<ViewResult>(result);
        Assert.False(_subnetController.ModelState.IsValid);

        Subnet? unchangedSubnet = await _context.Subnets.FindAsync([subnetId], TestContext.Current.CancellationToken);
        Assert.NotNull(unchangedSubnet);
        Assert.Equal(23, unchangedSubnet.Cidr);
    }

    [Fact]
    public async Task SetAllocationStatus_EmptySubnet_Succeeds()
    {

        int subnetId = 7;
        SubnetAllocationDto dto = new()
        {
            SubnetId = subnetId,
            IsFullyAllocated = true
        };

        IActionResult result = await _hostIpController.SetAllocationStatus(dto);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(subnetId, redirectResult.RouteValues?["id"]);

        Subnet? updatedSubnet = await _context.Subnets.FindAsync([subnetId], TestContext.Current.CancellationToken);
        Assert.NotNull(updatedSubnet);
        Assert.True(updatedSubnet.IsFullyAllocated);
    }

    [Fact]
    public async Task SetAllocationStatus_SubnetWithHostIps_Fails()
    {

        int subnetId = 1;
        SubnetAllocationDto dto = new()
        {
            SubnetId = subnetId,
            IsFullyAllocated = true
        };

        IActionResult result = await _hostIpController.SetAllocationStatus(dto);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(subnetId, redirectResult.RouteValues?["id"]);

        Assert.Contains("host IP assignments", _hostIpController.TempData["ErrorMessage"]?.ToString() ?? "");

        Subnet? unchangedSubnet = await _context.Subnets.FindAsync([subnetId], TestContext.Current.CancellationToken);
        Assert.NotNull(unchangedSubnet);
        Assert.False(unchangedSubnet.IsFullyAllocated);
    }

    [Fact]
    public async Task SetAllocationStatus_SubnetWithChildren_Fails()
    {

        int subnetId = 4;
        SubnetAllocationDto dto = new()
        {
            SubnetId = subnetId,
            IsFullyAllocated = true
        };

        IActionResult result = await _hostIpController.SetAllocationStatus(dto);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(subnetId, redirectResult.RouteValues?["id"]);

        Assert.Contains("child subnets", _hostIpController.TempData["ErrorMessage"]?.ToString() ?? "");

        Subnet? unchangedSubnet = await _context.Subnets.FindAsync([subnetId], TestContext.Current.CancellationToken);
        Assert.NotNull(unchangedSubnet);
        Assert.False(unchangedSubnet.IsFullyAllocated);
    }

    [Fact]
    public async Task CreateSubnet_UnderParentWithHostIps_IsRejected()
    {

        CreateSubnetViewModel viewModel = new()
        {
            Name = "Child under host-IP parent",
            NetworkAddress = "192.168.0.0",
            Cidr = 25,
            ParentSubnetId = 1
        };

        _context.ChangeTracker.Clear();

        IActionResult result = await _subnetController.Create(viewModel);

        _ = Assert.IsType<ViewResult>(result);
        Assert.False(_subnetController.ModelState.IsValid);

        Subnet? created = await _context.Subnets
            .FirstOrDefaultAsync(s => s.NetworkAddress == "192.168.0.0" && s.Cidr == 25, TestContext.Current.CancellationToken);
        Assert.Null(created);
    }

    [Fact]
    public async Task CreateSubnet_UnderFullyAllocatedParent_IsRejected()
    {

        Subnet parent = (await _context.Subnets.FindAsync([7], TestContext.Current.CancellationToken))!;
        parent.IsFullyAllocated = true;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        CreateSubnetViewModel viewModel = new()
        {
            Name = "Child under fully-allocated parent",
            NetworkAddress = "10.20.0.0",
            Cidr = 25,
            ParentSubnetId = 7
        };

        _context.ChangeTracker.Clear();

        IActionResult result = await _subnetController.Create(viewModel);

        _ = Assert.IsType<ViewResult>(result);
        Assert.False(_subnetController.ModelState.IsValid);

        Subnet? created = await _context.Subnets
            .FirstOrDefaultAsync(s => s.NetworkAddress == "10.20.0.0" && s.Cidr == 25, TestContext.Current.CancellationToken);
        Assert.Null(created);
    }

    [Fact]
    public async Task DeleteSubnet_WithNestedHostIps_ArchivesAllHostIps()
    {

        int subnetId = 4;

        _subnetController.TempData.Clear();

        int initialSubnetCount = await _context.Subnets.CountAsync(TestContext.Current.CancellationToken);
        int initialHostIpCount = await _context.HostIpAssignments.CountAsync(TestContext.Current.CancellationToken);
        int initialDeletedSubnetCount = await _context.DeletedSubnets.CountAsync(TestContext.Current.CancellationToken);
        int initialDeletedHostIpCount = await _context.DeletedHostIpAssignments.CountAsync(TestContext.Current.CancellationToken);

        IActionResult result = await _subnetController.DeleteConfirmed(subnetId, "approved", int.MaxValue, long.MaxValue);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirectResult.ActionName);

        Assert.Contains("successfully", _subnetController.TempData["SuccessMessage"]?.ToString() ?? "");

        int expectedDeletedSubnets = 3;
        Assert.Equal(initialSubnetCount - expectedDeletedSubnets, await _context.Subnets.CountAsync(TestContext.Current.CancellationToken));

        int expectedDeletedHostIps = 1;
        Assert.Equal(initialHostIpCount - expectedDeletedHostIps, await _context.HostIpAssignments.CountAsync(TestContext.Current.CancellationToken));

        Assert.Equal(initialDeletedSubnetCount + expectedDeletedSubnets, await _context.DeletedSubnets.CountAsync(TestContext.Current.CancellationToken));

        Assert.Equal(initialDeletedHostIpCount + expectedDeletedHostIps, await _context.DeletedHostIpAssignments.CountAsync(TestContext.Current.CancellationToken));

        DeletedHostIpAssignment? archivedHostIp = await _context.DeletedHostIpAssignments
            .FirstOrDefaultAsync(h => h.OriginalIP == "10.10.1.10", TestContext.Current.CancellationToken);
        Assert.NotNull(archivedHostIp);
        Assert.Equal(5, archivedHostIp.OriginalSubnetId);
        Assert.Equal("Host 7", archivedHostIp.Name);
    }

    private void SeedSubnetWithHostIp(int subnetId, string network, int cidr, string hostIp)
    {
        _context.Subnets.Add(new Subnet
        {
            Id = subnetId,
            Name = $"Subnet {subnetId}",
            NetworkAddress = network,
            Cidr = cidr,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        });
        _context.HostIpAssignments.Add(new HostIpAssignment
        {
            IP = hostIp,
            Name = "Host",
            SubnetId = subnetId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        });
        _context.SaveChanges();
    }

    [Fact]
    public void ValidateSubnetCidrChangeWithHostIps_HostIpBecomesBroadcastAddress_IsRejected()
    {
        SeedSubnetWithHostIp(100, "10.0.0.0", 24, "10.0.0.127");

        ValidationResult result = _hostIpValidationService
            .ValidateSubnetCidrChangeWithHostIps(100, "10.0.0.0", 24, 25);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("10.0.0.127"));
    }

    [Fact]
    public void ValidateSubnetCidrChangeWithHostIps_HostIpStillOrdinary_IsAllowed()
    {
        SeedSubnetWithHostIp(101, "10.1.0.0", 24, "10.1.0.10");

        ValidationResult result = _hostIpValidationService
            .ValidateSubnetCidrChangeWithHostIps(101, "10.1.0.0", 24, 25);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateSubnetCidrChangeWithHostIps_NarrowingToSlash31_DoesNotReserveEitherAddress()
    {
        SeedSubnetWithHostIp(102, "10.2.0.0", 30, "10.2.0.1");

        ValidationResult result = _hostIpValidationService
            .ValidateSubnetCidrChangeWithHostIps(102, "10.2.0.0", 30, 31);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateSubnetCidrChangeWithHostIps_HostIpFallsOutsideRange_IsStillRejected()
    {
        SeedSubnetWithHostIp(103, "10.3.0.0", 24, "10.3.0.200");

        ValidationResult result = _hostIpValidationService
            .ValidateSubnetCidrChangeWithHostIps(103, "10.3.0.0", 24, 25);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("outside the subnet range"));
    }

    [Fact]
    public void ValidateSubnetCidrChangeWithHostIps_WideningFromSlash31_HostIpBecomesNetworkAddress_IsRejected()
    {
        SeedSubnetWithHostIp(104, "10.4.0.0", 31, "10.4.0.0");

        ValidationResult result = _hostIpValidationService
            .ValidateSubnetCidrChangeWithHostIps(104, "10.4.0.0", 31, 30);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("10.4.0.0"));
        Assert.Contains(result.Errors, e => e.Message.Contains("network address"));
    }

    [Fact]
    public void ValidateSubnetCidrChangeWithHostIps_WideningFromSlash32_HostIpBecomesNetworkAddress_IsRejected()
    {
        SeedSubnetWithHostIp(105, "10.5.0.0", 32, "10.5.0.0");

        ValidationResult result = _hostIpValidationService
            .ValidateSubnetCidrChangeWithHostIps(105, "10.5.0.0", 32, 24);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("10.5.0.0"));
    }

    [Fact]
    public void ValidateSubnetCidrChangeWithHostIps_WideningFromSlash31_OtherAddress_IsAllowed()
    {
        SeedSubnetWithHostIp(106, "10.6.0.0", 31, "10.6.0.1");

        ValidationResult result = _hostIpValidationService
            .ValidateSubnetCidrChangeWithHostIps(106, "10.6.0.0", 31, 30);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateSubnetCidrChangeWithHostIps_WideningToSlash31_ReservesNothing()
    {
        SeedSubnetWithHostIp(107, "10.7.0.0", 32, "10.7.0.0");

        ValidationResult result = _hostIpValidationService
            .ValidateSubnetCidrChangeWithHostIps(107, "10.7.0.0", 32, 31);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateSubnetCidrChangeWithHostIps_OrdinaryWidening_IsAllowed()
    {
        SeedSubnetWithHostIp(108, "10.8.0.0", 30, "10.8.0.1");

        ValidationResult result = _hostIpValidationService
            .ValidateSubnetCidrChangeWithHostIps(108, "10.8.0.0", 30, 29);

        Assert.True(result.IsValid);
    }
    [Fact]
    public async Task DeleteConfirmed_WhenAChildWasAddedAfterTheReview_RefusesAndArchivesNothing()
    {
        _context.Subnets.Add(new Subnet { Id = 700, Name = "root", NetworkAddress = "10.70.0.0", Cidr = 16 });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        int reviewedMaxSubnetId = 0;

        _context.Subnets.Add(new Subnet
        {
            Id = 701, Name = "added-after", NetworkAddress = "10.70.1.0", Cidr = 24, ParentSubnetId = 700
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IActionResult result = await _subnetController.DeleteConfirmed(
            700, "approved", reviewedMaxSubnetId, long.MaxValue);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(await _context.Subnets.FindAsync([700], TestContext.Current.CancellationToken));
        Assert.NotNull(await _context.Subnets.FindAsync([701], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteConfirmed_WhenTheSubtreeShrank_StillCommits()
    {
        _context.Subnets.Add(new Subnet { Id = 710, Name = "root", NetworkAddress = "10.71.0.0", Cidr = 16 });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IActionResult result = await _subnetController.DeleteConfirmed(
            710, "approved", 999, long.MaxValue);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Null(await _context.Subnets.FindAsync([710], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteConfirmed_WithNoReviewedScope_RefusesAndArchivesNothing()
    {
        _context.Subnets.Add(new Subnet { Id = 720, Name = "root", NetworkAddress = "10.72.0.0", Cidr = 16 });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IActionResult result = await _subnetController.DeleteConfirmed(720, "approved", null, null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(await _context.Subnets.FindAsync([720], TestContext.Current.CancellationToken));
    }
}
