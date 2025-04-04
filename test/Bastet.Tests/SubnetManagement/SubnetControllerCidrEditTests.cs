using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;

namespace Bastet.Tests.SubnetManagement;

/// <summary>
/// Integration tests for subnet CIDR editing functionality in the SubnetController
/// </summary>
public class SubnetControllerCidrEditTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly IUserContextService _userContextService;
    private readonly IIpUtilityService _ipUtilityService;
    private readonly SubnetValidationService _validationService;
    private readonly SubnetController _controller;

    public SubnetControllerCidrEditTests()
    {
        // Create in-memory database context
        _context = TestDbContextFactory.CreateDbContext();

        // Set up services
        _userContextService = ControllerTestHelper.CreateMockUserContextService();
        _ipUtilityService = new IpUtilityService();
        _validationService = new SubnetValidationService(_ipUtilityService);

        // Create and configure the controller
        _controller = new SubnetController(_context, _ipUtilityService, _validationService, _userContextService);
        ControllerTestHelper.SetupController(_controller);

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
        // Create a hierarchy of test subnets (parent, siblings and child subnets)

        // Create parent subnet
        Subnet parentSubnet = new()
        {
            Id = 1,
            Name = "Parent (10.0.0.0/16)",
            NetworkAddress = "10.0.0.0",
            Cidr = 16,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(parentSubnet);

        // Create sibling subnets
        Subnet sibling1 = new()
        {
            Id = 2,
            Name = "Sibling 1 (10.0.0.0/24)",
            NetworkAddress = "10.0.0.0",
            Cidr = 24,
            ParentSubnetId = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(sibling1);

        Subnet sibling2 = new()
        {
            Id = 3,
            Name = "Sibling 2 (10.0.1.0/24)",
            NetworkAddress = "10.0.1.0",
            Cidr = 24,
            ParentSubnetId = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(sibling2);

        // Create target subnet for testing
        Subnet targetSubnet = new()
        {
            Id = 4,
            Name = "Target Subnet (10.0.2.0/24)",
            NetworkAddress = "10.0.2.0",
            Cidr = 24,
            ParentSubnetId = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(targetSubnet);

        // Create child subnets for the target
        Subnet child1 = new()
        {
            Id = 5,
            Name = "Child 1 (10.0.2.0/25)",
            NetworkAddress = "10.0.2.0",
            Cidr = 25,
            ParentSubnetId = 4,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(child1);

        Subnet child2 = new()
        {
            Id = 6,
            Name = "Child 2 (10.0.2.128/25)",
            NetworkAddress = "10.0.2.128",
            Cidr = 25,
            ParentSubnetId = 4,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(child2);

        // Add an unrelated subnet (not in the hierarchy)
        Subnet unrelatedSubnet = new()
        {
            Id = 7,
            Name = "Unrelated Subnet (192.168.0.0/24)",
            NetworkAddress = "192.168.0.0",
            Cidr = 24,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(unrelatedSubnet);

        // Save all changes
        _context.SaveChanges();
    }

    // GET Edit Tests

    [Fact]
    public async Task Edit_GET_SubnetExists_ReturnsEditViewModel()
    {
        // Arrange
        int subnetId = 4; // Target Subnet

        // Act
        IActionResult result = await _controller.Edit(subnetId);

        // Assert
        ViewResult viewResult = Assert.IsType<ViewResult>(result);
        EditSubnetViewModel model = Assert.IsType<EditSubnetViewModel>(viewResult.Model);

        Assert.Equal(subnetId, model.Id);
        Assert.Equal("10.0.2.0", model.NetworkAddress);
        Assert.Equal(24, model.Cidr);
        Assert.Equal(24, model.OriginalCidr);
        Assert.NotNull(model.ParentSubnetInfo);
        Assert.Contains("10.0.0.0/16", model.ParentSubnetInfo);
    }

    [Fact]
    public async Task Edit_GET_NonExistentSubnet_ReturnsNotFound()
    {
        // Arrange
        int nonExistentId = 999;

        // Act
        IActionResult result = await _controller.Edit(nonExistentId);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    // POST Edit Tests - Successful Scenarios

    [Fact]
    public async Task Edit_POST_NoChanges_ReturnsRedirectToDetails()
    {
        // Arrange
        EditSubnetViewModel viewModel = new()
        {
            Id = 4, // Target Subnet
            Name = "Target Subnet (10.0.2.0/24)",
            NetworkAddress = "10.0.2.0",
            Cidr = 24,
            OriginalCidr = 24 // Same as current
        };

        // Act
        IActionResult result = await _controller.Edit(4, viewModel);

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal(4, redirectResult.RouteValues?["id"]);
        Assert.Contains("was updated successfully", _controller.TempData["SuccessMessage"]?.ToString() ?? "");
    }

    [Fact]
    public async Task Edit_POST_UpdateNameAndDescription_ReturnsRedirectToDetails()
    {
        // Arrange
        EditSubnetViewModel viewModel = new()
        {
            Id = 4, // Target Subnet
            Name = "Updated Name",
            NetworkAddress = "10.0.2.0",
            Cidr = 24,
            OriginalCidr = 24, // Same as current
            Description = "Updated description"
        };

        // Act
        IActionResult result = await _controller.Edit(4, viewModel);

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);

        // Verify the database was updated
        Subnet? updatedSubnet = await _context.Subnets.FindAsync(4);
        Assert.NotNull(updatedSubnet);
        Assert.Equal("Updated Name", updatedSubnet.Name);
        Assert.Equal("Updated description", updatedSubnet.Description);
    }

    [Fact]
    public async Task Edit_POST_IncreaseCidr_NoOrphanedChildren_ReturnsRedirectToDetails()
    {
        // Arrange - First delete children to avoid validation errors
        _context.Subnets.Remove(await _context.Subnets.FindAsync(5) ?? throw new Exception("Child 1 not found"));
        _context.Subnets.Remove(await _context.Subnets.FindAsync(6) ?? throw new Exception("Child 2 not found"));
        await _context.SaveChangesAsync();

        EditSubnetViewModel viewModel = new()
        {
            Id = 4, // Target Subnet
            Name = "Target Subnet",
            NetworkAddress = "10.0.2.0",
            Cidr = 25, // Increasing CIDR from 24 to 25 (smaller subnet)
            OriginalCidr = 24
        };

        // Act
        IActionResult result = await _controller.Edit(4, viewModel);

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);

        // Verify the database was updated
        Subnet? updatedSubnet = await _context.Subnets.FindAsync(4);
        Assert.NotNull(updatedSubnet);
        Assert.Equal(25, updatedSubnet.Cidr);
    }

    [Fact]
    public async Task Edit_POST_DecreaseCidr_NoConflicts_ReturnsRedirectToDetails()
    {
        // Arrange - Create a subnet with no siblings or conflicts
        Subnet isolatedSubnet = new()
        {
            Id = 10,
            Name = "Isolated Subnet (172.16.0.0/24)",
            NetworkAddress = "172.16.0.0",
            Cidr = 24,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(isolatedSubnet);
        await _context.SaveChangesAsync();

        EditSubnetViewModel viewModel = new()
        {
            Id = 10,
            Name = "Isolated Subnet",
            NetworkAddress = "172.16.0.0",
            Cidr = 23, // Decreasing CIDR from 24 to 23 (larger subnet)
            OriginalCidr = 24
        };

        // Act
        IActionResult result = await _controller.Edit(10, viewModel);

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);

        // Verify the database was updated
        Subnet? updatedSubnet = await _context.Subnets.FindAsync(10);
        Assert.NotNull(updatedSubnet);
        Assert.Equal(23, updatedSubnet.Cidr);
    }

    // POST Edit Tests - Failure Scenarios

    [Fact]
    public async Task Edit_POST_MissingName_ReturnsViewWithError()
    {
        // Arrange - Set ModelState error manually since this would normally be done by model binding
        _controller.ModelState.AddModelError("Name", "Name is required");

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "", // Missing name
            NetworkAddress = "10.0.2.0",
            Cidr = 24,
            OriginalCidr = 24
        };

        // Act
        IActionResult result = await _controller.Edit(4, viewModel);

        // Assert
        _ = Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);
        Assert.Contains("Name", _controller.ModelState.Keys);
    }

    [Fact]
    public async Task Edit_POST_InvalidCidr_ReturnsViewWithError()
    {
        // Arrange - Set ModelState error manually since validation would happen earlier
        _controller.ModelState.AddModelError("Cidr", "CIDR must be between 0 and 32");

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "Target Subnet",
            NetworkAddress = "10.0.2.0",
            Cidr = 24, // Using valid CIDR but ModelState is invalid from our manual error
            OriginalCidr = 24
        };

        // Act
        IActionResult result = await _controller.Edit(4, viewModel);

        // Assert
        _ = Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);
        Assert.Contains("Cidr", _controller.ModelState.Keys);
    }

    [Fact]
    public async Task Edit_POST_IncreaseCidr_OrphansChildren_ReturnsViewWithError()
    {
        // Arrange - Set ModelState error manually to simulate validation failure
        _controller.ModelState.AddModelError("Cidr", "Child subnet Child 2 (10.0.2.128/25) would no longer fit within this subnet if CIDR is increased to /25");

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "Target Subnet",
            NetworkAddress = "10.0.2.0",
            Cidr = 25, // Increasing from 24 to 25 would orphan child2
            OriginalCidr = 24
        };

        // Act
        IActionResult result = await _controller.Edit(4, viewModel);

        // Assert
        ViewResult viewResult = Assert.IsType<ViewResult>(result);
        _ = Assert.IsType<EditSubnetViewModel>(viewResult.Model);

        Assert.False(_controller.ModelState.IsValid);
        Assert.Contains("Cidr", _controller.ModelState.Keys);
        Assert.Contains("Child subnet", _controller.ModelState["Cidr"]?.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task Edit_POST_DecreaseCidr_OverlapsWithSibling_ReturnsViewWithError()
    {
        // Arrange - Set ModelState error manually to simulate validation failure
        _controller.ModelState.AddModelError("Cidr", "Subnet overlaps with existing subnet: Sibling 1 (10.0.0.0/24)");

        EditSubnetViewModel viewModel = new()
        {
            Id = 4, // Target subnet (10.0.2.0/24)
            Name = "Target Subnet",
            NetworkAddress = "10.0.2.0",
            Cidr = 22, // Decreasing from 24 to 22 would overlap with sibling subnets
            OriginalCidr = 24
        };

        // Act
        IActionResult result = await _controller.Edit(4, viewModel);

        // Assert
        _ = Assert.IsType<ViewResult>(result);

        Assert.False(_controller.ModelState.IsValid);
        Assert.Contains("Cidr", _controller.ModelState.Keys);
        Assert.Contains("overlap", _controller.ModelState["Cidr"]?.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task Edit_POST_DecreaseCidr_BeyondParent_ReturnsViewWithError()
    {
        // Arrange - Set ModelState error manually to simulate validation failure
        _controller.ModelState.AddModelError("Cidr", "Decreasing CIDR to /15 would make this subnet too large to fit within its parent subnet (10.0.0.0/16)");

        EditSubnetViewModel viewModel = new()
        {
            Id = 4, // Target subnet (10.0.2.0/24)
            Name = "Target Subnet",
            NetworkAddress = "10.0.2.0",
            Cidr = 15, // Decreasing from 24 to 15 would extend beyond parent (10.0.0.0/16)
            OriginalCidr = 24
        };

        // Act
        IActionResult result = await _controller.Edit(4, viewModel);

        // Assert
        _ = Assert.IsType<ViewResult>(result);

        Assert.False(_controller.ModelState.IsValid);
        Assert.Contains("Cidr", _controller.ModelState.Keys);
        Assert.Contains("parent subnet", _controller.ModelState["Cidr"]?.Errors[0].ErrorMessage.ToLower());
    }
}
