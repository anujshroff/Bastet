using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.DTOs;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Tests.HostIpManagement;

/// <summary>
/// Tests the interactions between subnet operations and host IP assignments
/// Including CIDR modifications, subnet deletion, and full allocation
/// </summary>
public class SubnetHostIpInteractionTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly IUserContextService _userContextService;
    private readonly IIpUtilityService _ipUtilityService;
    private readonly SubnetValidationService _subnetValidationService;
    private readonly HostIpValidationService _hostIpValidationService;
    private readonly SubnetController _subnetController;
    private readonly HostIpController _hostIpController;

    public SubnetHostIpInteractionTests()
    {
        // Create in-memory database context
        _context = TestDbContextFactory.CreateDbContext();

        // Create services
        _userContextService = ControllerTestHelper.CreateMockUserContextService();
        _ipUtilityService = new IpUtilityService();
        _subnetValidationService = new SubnetValidationService(_ipUtilityService);
        _hostIpValidationService = new HostIpValidationService(_ipUtilityService, _context);

        // Create controllers
        _subnetController = new SubnetController(_context, _ipUtilityService,
            _subnetValidationService, _hostIpValidationService, _userContextService,
            ControllerTestHelper.CreateMockSubnetLockingService());
        ControllerTestHelper.SetupController(_subnetController);

        _hostIpController = new HostIpController(_context, _hostIpValidationService,
            _ipUtilityService, _userContextService);
        ControllerTestHelper.SetupController(_hostIpController);

        // Set up test data
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
        // Create subnets for various scenarios

        // Subnet that can be expanded (192.168.0.0/24)
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

        // Add host IPs to the expandable subnet
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

        // Subnet that can be shrunk (172.16.0.0/23) to (172.16.0.0/24)
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

        // Add host IPs to the first half of the shrinkable subnet
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

        // Subnet that cannot be shrunk (10.0.0.0/23) to (10.0.0.0/24)
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

        // Add host IPs to both halves of the unshrinkable subnet
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

        // Parent subnet for deletion testing (10.10.0.0/16)
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

        // Child subnet 1 (10.10.1.0/24)
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

        // Host IPs in child subnet 1
        HostIpAssignment hostIp7 = new()
        {
            IP = "10.10.1.10",
            Name = "Host 7",
            SubnetId = 5,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };
        _context.HostIpAssignments.Add(hostIp7);

        // Child subnet 2 (10.10.2.0/24)
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

        // Empty subnet for allocation testing (10.20.0.0/24)
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
        // Arrange - Expand 192.168.0.0/24 to 192.168.0.0/23
        // Note: 192.168.0.0 works for both /24 and /23 subnets
        int subnetId = 1;
        EditSubnetViewModel viewModel = new()
        {
            Id = subnetId,
            Name = "Expandable Subnet",
            NetworkAddress = "192.168.0.0", // Correct network address for a /23
            Cidr = 23, // Decrease from 24 to 23 (expand)
            OriginalCidr = 24,
            Description = "Expanded subnet"
        };

        // Act
        IActionResult result = await _subnetController.Edit(subnetId, viewModel);

        // Assert - This should be a successful operation
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(subnetId, redirectResult.RouteValues?["id"]);

        // Verify database was updated
        Subnet? updatedSubnet = await _context.Subnets.FindAsync(subnetId);
        Assert.NotNull(updatedSubnet);
        Assert.Equal(23, updatedSubnet.Cidr); // CIDR was decreased from 24 to 23
    }

    [Fact]
    public async Task EditSubnet_IncreaseCidr_AllHostIpsStillInRange_Succeeds()
    {
        // Arrange - Shrink 172.16.0.0/23 to 172.16.0.0/24
        // (All host IPs are in first half of subnet, so this is valid)
        int subnetId = 2;
        EditSubnetViewModel viewModel = new()
        {
            Id = subnetId,
            Name = "Shrinkable Subnet",
            NetworkAddress = "172.16.0.0",
            Cidr = 24, // Increase from 23 to 24 (shrink)
            OriginalCidr = 23,
            Description = "Shrunk subnet"
        };

        // Act
        IActionResult result = await _subnetController.Edit(subnetId, viewModel);

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(subnetId, redirectResult.RouteValues?["id"]);

        // Verify database was updated
        Subnet? updatedSubnet = await _context.Subnets.FindAsync(subnetId);
        Assert.NotNull(updatedSubnet);
        Assert.Equal(24, updatedSubnet.Cidr);
    }

    [Fact]
    public async Task EditSubnet_IncreaseCidr_HostIpOutOfRange_Fails()
    {
        // Arrange - Try to shrink 10.0.0.0/23 to 10.0.0.0/24
        // but there are host IPs in second half of subnet, so this should fail
        int subnetId = 3;
        EditSubnetViewModel viewModel = new()
        {
            Id = subnetId,
            Name = "Unshrinkable Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 24, // Increase from 23 to 24 (shrink)
            OriginalCidr = 23,
            Description = "Attempted to shrink"
        };

        // Act
        IActionResult result = await _subnetController.Edit(subnetId, viewModel);

        // Assert
        _ = Assert.IsType<ViewResult>(result);
        Assert.False(_subnetController.ModelState.IsValid);

        // Verify database was not updated
        Subnet? unchangedSubnet = await _context.Subnets.FindAsync(subnetId);
        Assert.NotNull(unchangedSubnet);
        Assert.Equal(23, unchangedSubnet.Cidr); // CIDR remains unchanged
    }

    [Fact]
    public async Task SetAllocationStatus_EmptySubnet_Succeeds()
    {
        // Arrange
        int subnetId = 7; // Empty subnet
        SubnetAllocationDto dto = new()
        {
            SubnetId = subnetId,
            IsFullyAllocated = true
        };

        // Act
        IActionResult result = await _hostIpController.SetAllocationStatus(dto);

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(subnetId, redirectResult.RouteValues?["id"]);

        // Verify database was updated
        Subnet? updatedSubnet = await _context.Subnets.FindAsync(subnetId);
        Assert.NotNull(updatedSubnet);
        Assert.True(updatedSubnet.IsFullyAllocated);
    }

    [Fact]
    public async Task SetAllocationStatus_SubnetWithHostIps_Fails()
    {
        // Arrange
        int subnetId = 1; // Subnet with host IPs
        SubnetAllocationDto dto = new()
        {
            SubnetId = subnetId,
            IsFullyAllocated = true
        };

        // Act
        IActionResult result = await _hostIpController.SetAllocationStatus(dto);

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(subnetId, redirectResult.RouteValues?["id"]);

        // Verify error message in TempData
        Assert.Contains("host IP assignments", _hostIpController.TempData["ErrorMessage"]?.ToString() ?? "");

        // Verify database was not updated
        Subnet? unchangedSubnet = await _context.Subnets.FindAsync(subnetId);
        Assert.NotNull(unchangedSubnet);
        Assert.False(unchangedSubnet.IsFullyAllocated); // Still not fully allocated
    }

    [Fact]
    public async Task SetAllocationStatus_SubnetWithChildren_Fails()
    {
        // Arrange
        int subnetId = 4; // Parent subnet with child subnets
        SubnetAllocationDto dto = new()
        {
            SubnetId = subnetId,
            IsFullyAllocated = true
        };

        // Act
        IActionResult result = await _hostIpController.SetAllocationStatus(dto);

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(subnetId, redirectResult.RouteValues?["id"]);

        // Verify error message in TempData
        Assert.Contains("child subnets", _hostIpController.TempData["ErrorMessage"]?.ToString() ?? "");

        // Verify database was not updated
        Subnet? unchangedSubnet = await _context.Subnets.FindAsync(subnetId);
        Assert.NotNull(unchangedSubnet);
        Assert.False(unchangedSubnet.IsFullyAllocated); // Still not fully allocated
    }

    [Fact]
    public async Task DeleteSubnet_WithNestedHostIps_ArchivesAllHostIps()
    {
        // Arrange
        int subnetId = 4; // Parent subnet with children that have host IPs

        // First simulate a form submission with "approved" confirmation
        _subnetController.TempData.Clear();

        // Record initial counts
        int initialSubnetCount = await _context.Subnets.CountAsync();
        int initialHostIpCount = await _context.HostIpAssignments.CountAsync();
        int initialDeletedSubnetCount = await _context.DeletedSubnets.CountAsync();
        int initialDeletedHostIpCount = await _context.DeletedHostIpAssignments.CountAsync();

        // Act
        IActionResult result = await _subnetController.DeleteConfirmed(subnetId, "approved");

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirectResult.ActionName);

        // Verify success message
        Assert.Contains("successfully", _subnetController.TempData["SuccessMessage"]?.ToString() ?? "");

        // Verify subnets were deleted from main table
        int expectedDeletedSubnets = 3; // Parent + 2 children
        Assert.Equal(initialSubnetCount - expectedDeletedSubnets, await _context.Subnets.CountAsync());

        // Verify host IPs were deleted from main table
        int expectedDeletedHostIps = 1; // One host IP in child subnet
        Assert.Equal(initialHostIpCount - expectedDeletedHostIps, await _context.HostIpAssignments.CountAsync());

        // Verify subnets were archived in deleted table
        Assert.Equal(initialDeletedSubnetCount + expectedDeletedSubnets, await _context.DeletedSubnets.CountAsync());

        // Verify host IPs were archived in deleted table
        Assert.Equal(initialDeletedHostIpCount + expectedDeletedHostIps, await _context.DeletedHostIpAssignments.CountAsync());

        // Check that the host IP specifically was archived with the correct original subnet ID
        DeletedHostIpAssignment? archivedHostIp = await _context.DeletedHostIpAssignments
            .FirstOrDefaultAsync(h => h.OriginalIP == "10.10.1.10");
        Assert.NotNull(archivedHostIp);
        Assert.Equal(5, archivedHostIp.OriginalSubnetId); // Child subnet ID
        Assert.Equal("Host 7", archivedHostIp.Name);
    }
}
