using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Security;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly IInputSanitizationService _sanitizationService;
    private readonly SubnetController _controller;

    public SubnetControllerCidrEditTests()
    {
        // Create in-memory database context
        _context = TestDbContextFactory.CreateDbContext();

        // Set up services
        _userContextService = ControllerTestHelper.CreateMockUserContextService();
        _ipUtilityService = new IpUtilityService();
        _validationService = new SubnetValidationService(_ipUtilityService);
        _sanitizationService = new InputSanitizationService();

        // Need HostIpValidationService for the updated controller signature
        HostIpValidationService hostIpValidationService = new(_ipUtilityService, _context);

        // Create and configure the controller
        _controller = new SubnetController(_context, _ipUtilityService, _validationService, hostIpValidationService, _userContextService, ControllerTestHelper.CreateMockSubnetLockingService(), NullLogger<SubnetController>.Instance);
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

        // Safely check parent subnet info
        string? parentInfo = model.ParentSubnetInfo;
        Assert.NotNull(parentInfo);
        Assert.Contains("10.0.0.0/16", parentInfo);
    }

    [Fact]
    public async Task Edit_GET_NonExistentSubnet_RedirectsToNotFoundError()
    {
        // Arrange
        int nonExistentId = 999;

        // Act
        IActionResult result = await _controller.Edit(nonExistentId);

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("HttpStatusCodeHandler", redirectResult.ActionName);
        Assert.Equal("Error", redirectResult.ControllerName);

        // Safe access to route values dictionary
        object? statusCode = redirectResult.RouteValues?["statusCode"];
        Assert.NotNull(statusCode);
        Assert.Equal(404, statusCode);

        // The custom message travels via TempData, not the (forgeable) query string, and is keyed
        // to this redirect so a concurrent 4xx elsewhere in the session cannot take it.
        string errorMessageStr = ErrorPageMessages.Take(
            _controller.TempData, redirectResult.RouteValues?["m"]?.ToString()) ?? string.Empty;
        Assert.Contains($"{nonExistentId}", errorMessageStr);
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

        // Safe handling of route values
        object? idValue = redirectResult.RouteValues?["id"];
        Assert.NotNull(idValue);
        Assert.Equal(4, idValue);

        // Safe handling of TempData
        string? successMessage = _controller.TempData["SuccessMessage"]?.ToString();
        Assert.NotNull(successMessage);
        Assert.Contains("was updated successfully", successMessage);
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
        Subnet? updatedSubnet = await _context.Subnets.FindAsync([4], TestContext.Current.CancellationToken);
        Assert.NotNull(updatedSubnet);

        // Use null-safe accessors for properties
        string? name = updatedSubnet.Name;
        string? description = updatedSubnet.Description;
        Assert.NotNull(name);
        Assert.NotNull(description);
        Assert.Equal("Updated Name", name);
        Assert.Equal("Updated description", description);
    }

    [Fact]
    public async Task Edit_POST_IncreaseCidr_NoOrphanedChildren_ReturnsRedirectToDetails()
    {
        // Arrange - First delete children to avoid validation errors
        _context.Subnets.Remove(await _context.Subnets.FindAsync([5], TestContext.Current.CancellationToken) ?? throw new Exception("Child 1 not found"));
        _context.Subnets.Remove(await _context.Subnets.FindAsync([6], TestContext.Current.CancellationToken) ?? throw new Exception("Child 2 not found"));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

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
        Subnet? updatedSubnet = await _context.Subnets.FindAsync([4], TestContext.Current.CancellationToken);
        Assert.NotNull(updatedSubnet);
        int cidr = updatedSubnet.Cidr; // Safely access cidr value
        Assert.Equal(25, cidr);
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
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

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
        Subnet? updatedSubnet = await _context.Subnets.FindAsync([10], TestContext.Current.CancellationToken);
        Assert.NotNull(updatedSubnet);
        int cidr = updatedSubnet.Cidr; // Safely access cidr value
        Assert.Equal(23, cidr);
    }

    /// <summary>
    /// Pins the controller half of the /31-widening fix. The host-IP validator is only reached when
    /// the CIDR-change gate lets a decrease through, so a gate that skips decreases leaves the
    /// service rule unreachable and every service-level test still green. 10.20.0.0 is a legal
    /// assignment in a /31 and becomes the network address of the /30.
    /// </summary>
    [Fact]
    public async Task Edit_POST_DecreaseCidrFromSlash31_HostIpOnNetworkAddress_ReturnsViewWithError()
    {
        Subnet pointToPoint = new()
        {
            Id = 20,
            Name = "P2P link",
            NetworkAddress = "10.20.0.0",
            Cidr = 31,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(pointToPoint);
        _context.HostIpAssignments.Add(new HostIpAssignment
        {
            IP = "10.20.0.0",
            Name = "link-a",
            SubnetId = 20,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        EditSubnetViewModel viewModel = new()
        {
            Id = 20,
            Name = "P2P link",
            NetworkAddress = "10.20.0.0",
            Cidr = 30,
            OriginalCidr = 31
        };

        IActionResult result = await _controller.Edit(20, viewModel);

        // The form comes back rather than redirecting, and nothing was written.
        _ = Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);

        Subnet? unchanged = await _context.Subnets.FindAsync([20], TestContext.Current.CancellationToken);
        Assert.NotNull(unchanged);
        Assert.Equal(31, unchanged.Cidr);
    }

    [Fact]
    public async Task Edit_POST_DecreaseCidr_WithGrandparentAndGrandchild_ReturnsRedirectToDetails()
    {
        // The seeded target (10.0.2.0/24) sits under 10.0.0.0/16; give it a grandparent and a
        // grandchild so the expansion has hierarchy on both sides of it. Neither is an overlap, but
        // the whole-system sweep used to only exempt direct relatives and rejected the edit.
        Subnet grandparent = new()
        {
            Id = 11,
            Name = "Grandparent (10.0.0.0/8)",
            NetworkAddress = "10.0.0.0",
            Cidr = 8,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(grandparent);

        Subnet grandchild = new()
        {
            Id = 12,
            Name = "Grandchild (10.0.2.0/26)",
            NetworkAddress = "10.0.2.0",
            Cidr = 26,
            ParentSubnetId = 5, // Child 1 (10.0.2.0/25)
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(grandchild);

        Subnet parent = (await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken))!;
        parent.ParentSubnetId = 11;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // 10.0.2.0/24 -> /23 covers 10.0.2.0-10.0.3.255: inside the parent, clear of both siblings.
        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "Target Subnet (10.0.2.0/24)",
            NetworkAddress = "10.0.2.0",
            Cidr = 23,
            OriginalCidr = 24
        };

        IActionResult result = await _controller.Edit(4, viewModel);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);

        Subnet? updatedSubnet = await _context.Subnets.FindAsync([4], TestContext.Current.CancellationToken);
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

    /// <summary>
    /// The out-of-range value must survive to be redisplayed with its message. The [Range(0,32)]
    /// failure skips the guarded block entirely, so execution falls through to the mask calculation
    /// at the tail of the action - which sits outside any try and throws for a CIDR it cannot
    /// compute, turning a form validation error into a 500.
    /// </summary>
    [Theory]
    [InlineData(33)]
    [InlineData(99)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task Edit_POST_OutOfRangeCidr_ReturnsViewInsteadOfThrowing(int cidr)
    {
        _controller.ModelState.AddModelError("Cidr", "CIDR must be between 0 and 32");

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "Target Subnet",
            NetworkAddress = "10.0.2.0",
            Cidr = cidr,
            OriginalCidr = 24
        };

        IActionResult result = await _controller.Edit(4, viewModel);

        _ = Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);
        Assert.Contains("Cidr", _controller.ModelState.Keys);

        // The value the operator typed is redisplayed rather than silently clamped.
        Assert.Equal(cidr, viewModel.Cidr);
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
        // Safer access to errors collection with null checks
        Microsoft.AspNetCore.Mvc.ModelBinding.ModelErrorCollection? cidrErrors = _controller.ModelState["Cidr"]?.Errors;
        Assert.NotNull(cidrErrors);
        Assert.NotEmpty(cidrErrors);
        Assert.Contains("Child subnet", cidrErrors.First().ErrorMessage ?? string.Empty);
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
        // Safer access to errors collection with null checks
        Microsoft.AspNetCore.Mvc.ModelBinding.ModelErrorCollection? cidrErrors = _controller.ModelState["Cidr"]?.Errors;
        Assert.NotNull(cidrErrors);
        Assert.NotEmpty(cidrErrors);
        Assert.Contains("parent subnet", cidrErrors.First().ErrorMessage?.ToLower() ?? string.Empty);
    }

    [Fact]
    public async Task Edit_POST_MisalignedNetworkAddress_ReturnsViewWithError()
    {
        // Arrange - Set ModelState error manually to simulate validation failure
        _controller.ModelState.AddModelError("NetworkAddress", "Network address is not valid for the given CIDR. The network address must align with the subnet boundary.");

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "Target Subnet",
            NetworkAddress = "10.0.2.1", // Misaligned for /24 (should be 10.0.2.0)
            Cidr = 24,
            OriginalCidr = 24
        };

        // Act
        IActionResult result = await _controller.Edit(4, viewModel);

        // Assert
        _ = Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);
        Assert.Contains("NetworkAddress", _controller.ModelState.Keys);
        // Safer access to errors collection with null checks
        Microsoft.AspNetCore.Mvc.ModelBinding.ModelErrorCollection? networkAddressErrors = _controller.ModelState["NetworkAddress"]?.Errors;
        Assert.NotNull(networkAddressErrors);
        Assert.NotEmpty(networkAddressErrors);
        Assert.Contains("subnet boundary", networkAddressErrors.First().ErrorMessage?.ToLower() ?? string.Empty);
    }

    [Fact]
    public async Task Edit_POST_DecreaseCidr_OverlapsWithUnrelatedSubnet_ReturnsViewWithError()
    {
        // First, create an unrelated subnet that would overlap with our target if expanded
        Subnet unrelatedOverlapSubnet = new()
        {
            Id = 20,
            Name = "Unrelated Subnet",
            NetworkAddress = "10.0.3.0", // Would overlap if target is expanded from /24 to /22
            Cidr = 24,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(unrelatedOverlapSubnet);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Arrange - Set ModelState error manually to simulate validation failure
        _controller.ModelState.AddModelError("Cidr", "Expanding to 10.0.2.0/22 would conflict with existing subnet: Unrelated Subnet (10.0.3.0/24)");

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "Target Subnet",
            NetworkAddress = "10.0.2.0",
            Cidr = 22, // Decreasing from /24 to /22 would overlap with unrelated subnet
            OriginalCidr = 24
        };

        // Act
        IActionResult result = await _controller.Edit(4, viewModel);

        // Assert
        _ = Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);
        Assert.Contains("Cidr", _controller.ModelState.Keys);
        Microsoft.AspNetCore.Mvc.ModelBinding.ModelErrorCollection? errors = _controller.ModelState["Cidr"]?.Errors;
        Assert.NotNull(errors);
        Assert.NotEmpty(errors);
        Assert.Contains("conflict with existing subnet", errors.First().ErrorMessage ?? string.Empty);
    }

    [Fact]
    public async Task Edit_POST_IncreaseCidr_ExactlyFitsChildren_ReturnsRedirectToDetails()
    {
        // Arrange - First reconfigure our test data to create a boundary case scenario

        // First, remove existing children
        _context.Subnets.Remove(await _context.Subnets.FindAsync([5], TestContext.Current.CancellationToken) ?? throw new Exception("Child 1 not found"));
        _context.Subnets.Remove(await _context.Subnets.FindAsync([6], TestContext.Current.CancellationToken) ?? throw new Exception("Child 2 not found"));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Then add children that would exactly fit in a /24 subnet
        Subnet newChild1 = new()
        {
            Id = 15,
            Name = "New Child 1",
            NetworkAddress = "10.0.2.0",
            Cidr = 25,
            ParentSubnetId = 4,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        Subnet newChild2 = new()
        {
            Id = 16,
            Name = "New Child 2",
            NetworkAddress = "10.0.2.128",
            Cidr = 25,
            ParentSubnetId = 4,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(newChild1);
        _context.Subnets.Add(newChild2);

        // Adjust our target subnet to be /23 so we can decrease its size to /24
        Subnet? targetSubnet = await _context.Subnets.FindAsync([4], TestContext.Current.CancellationToken) ?? throw new Exception("Target subnet not found");
        targetSubnet.Cidr = 23;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "Target Subnet",
            NetworkAddress = "10.0.2.0",
            Cidr = 24, // Increasing from /23 to /24, exactly fitting children
            OriginalCidr = 23
        };

        // Act
        IActionResult result = await _controller.Edit(4, viewModel);

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);

        // Verify the database was updated
        Subnet? updatedSubnet = await _context.Subnets.FindAsync([4], TestContext.Current.CancellationToken);
        Assert.NotNull(updatedSubnet);
        int cidr = updatedSubnet.Cidr; // Safely access cidr value
        Assert.Equal(24, cidr);
    }

    [Fact]
    public async Task Edit_POST_MultipleValidationErrors_ReturnsViewWithAllErrors()
    {
        // Arrange - Set multiple ModelState errors
        _controller.ModelState.AddModelError("Name", "Name is required");
        _controller.ModelState.AddModelError("Cidr", "CIDR must be between 0 and 32");
        _controller.ModelState.AddModelError("Description", "Description cannot be longer than 1000 characters");

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "", // Missing name
            NetworkAddress = "10.0.2.0",
            Cidr = 24, // Using valid CIDR but ModelState is invalid from our manual error
            OriginalCidr = 24,
            // Over the 1000-character limit the column and EditSubnetViewModel both carry. The value
            // is inert here - all three errors above are hand-injected - but a fixture that says
            // "too long" while holding a perfectly valid length is a trap for the next reader.
            Description = new string('x', 1100)
        };

        // Act
        IActionResult result = await _controller.Edit(4, viewModel);

        // Assert
        _ = Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);
        Assert.Equal(3, _controller.ModelState.ErrorCount);
        Assert.Contains("Name", _controller.ModelState.Keys);
        Assert.Contains("Cidr", _controller.ModelState.Keys);
        Assert.Contains("Description", _controller.ModelState.Keys);
    }

    /// <summary>
    /// An Azure-linked row records the prefix its resource had at link time, and the reconciler reads
    /// any difference from Azure's current prefix as Azure-side drift. Editing the CIDR here breaks
    /// that invariant silently and a later reconcile offers the row - and its whole subtree - for
    /// archival while Azure is healthy, so the edit must be refused rather than validated.
    /// </summary>
    [Theory]
    [InlineData(17)] // narrowing
    [InlineData(15)] // widening
    public async Task Edit_POST_CidrChangeOnAzureLinkedSubnet_IsRefusedAndWritesNothing(int newCidr)
    {
        _context.Subnets.Add(new Subnet
        {
            Id = 30,
            Name = "azure-vnet",
            NetworkAddress = "10.30.0.0",
            Cidr = 16,
            AzureResourceId = "/subscriptions/s/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/azure-vnet",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        EditSubnetViewModel viewModel = new()
        {
            Id = 30,
            Name = "azure-vnet",
            NetworkAddress = "10.30.0.0",
            Cidr = newCidr,
            OriginalCidr = 16
        };

        IActionResult result = await _controller.Edit(30, viewModel);

        _ = Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);
        Assert.Contains("Cidr", _controller.ModelState.Keys);
        Assert.Contains("Azure", _controller.ModelState["Cidr"]?.Errors.First().ErrorMessage ?? string.Empty);

        Subnet? unchanged = await _context.Subnets.FindAsync([30], TestContext.Current.CancellationToken);
        Assert.NotNull(unchanged);
        Assert.Equal(16, unchanged.Cidr);
    }

    /// <summary>
    /// The guard is on the CIDR only. Renaming, re-describing and re-tagging an imported subnet are
    /// ordinary operations and must keep working, or the fix costs more than the defect.
    /// </summary>
    [Fact]
    public async Task Edit_POST_NameChangeOnAzureLinkedSubnet_StillSucceeds()
    {
        _context.Subnets.Add(new Subnet
        {
            Id = 31,
            Name = "azure-vnet",
            NetworkAddress = "10.31.0.0",
            Cidr = 16,
            AzureResourceId = "/subscriptions/s/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/azure-vnet",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        EditSubnetViewModel viewModel = new()
        {
            Id = 31,
            Name = "renamed locally",
            NetworkAddress = "10.31.0.0",
            Cidr = 16, // unchanged
            OriginalCidr = 16,
            Description = "still editable"
        };

        IActionResult result = await _controller.Edit(31, viewModel);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);

        Subnet? updated = await _context.Subnets.FindAsync([31], TestContext.Current.CancellationToken);
        Assert.NotNull(updated);
        Assert.Equal("renamed locally", updated.Name);
        Assert.Equal("still editable", updated.Description);
        Assert.Equal(16, updated.Cidr);
    }

    /// <summary>
    /// The other half of the guard: a subnet with no Azure link keeps its editable CIDR. Without this
    /// a fix that simply froze every CIDR would pass the test above.
    /// </summary>
    [Fact]
    public async Task Edit_POST_CidrChangeOnUnlinkedSubnet_StillSucceeds()
    {
        _context.Subnets.Add(new Subnet
        {
            Id = 32,
            Name = "local only",
            NetworkAddress = "10.32.0.0",
            Cidr = 16,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        EditSubnetViewModel viewModel = new()
        {
            Id = 32,
            Name = "local only",
            NetworkAddress = "10.32.0.0",
            Cidr = 17,
            OriginalCidr = 16
        };

        IActionResult result = await _controller.Edit(32, viewModel);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);

        Subnet? updated = await _context.Subnets.FindAsync([32], TestContext.Current.CancellationToken);
        Assert.NotNull(updated);
        Assert.Equal(17, updated.Cidr);
    }

    /// <summary>
    /// The form must say so before the operator types, not only after they save. The flag is derived
    /// from the database on render, never bound from the post.
    /// </summary>
    [Fact]
    public async Task Edit_GET_AzureLinkedSubnet_MarksTheModelAsLinked()
    {
        _context.Subnets.Add(new Subnet
        {
            Id = 33,
            Name = "azure-vnet",
            NetworkAddress = "10.33.0.0",
            Cidr = 16,
            AzureResourceId = "/subscriptions/s/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/azure-vnet",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        ViewResult linked = Assert.IsType<ViewResult>(await _controller.Edit(33));
        Assert.True(Assert.IsType<EditSubnetViewModel>(linked.Model).IsAzureLinked);

        ViewResult unlinked = Assert.IsType<ViewResult>(await _controller.Edit(4));
        Assert.False(Assert.IsType<EditSubnetViewModel>(unlinked.Model).IsAzureLinked);
    }

    [Fact]
    public async Task Edit_POST_NonExistentSubnet_RedirectsToNotFoundError()
    {
        // Arrange
        int nonExistentId = 999;
        EditSubnetViewModel viewModel = new()
        {
            Id = nonExistentId,
            Name = "Non-existent Subnet",
            NetworkAddress = "10.1.1.0",
            Cidr = 24,
            OriginalCidr = 24
        };

        // Act
        IActionResult result = await _controller.Edit(nonExistentId, viewModel);

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("HttpStatusCodeHandler", redirectResult.ActionName);
        Assert.Equal("Error", redirectResult.ControllerName);

        // Safe access to route values dictionary
        object? statusCode = redirectResult.RouteValues?["statusCode"];
        Assert.NotNull(statusCode);
        Assert.Equal(404, statusCode);

        // The custom message travels via TempData, not the (forgeable) query string, and is keyed
        // to this redirect so a concurrent 4xx elsewhere in the session cannot take it.
        string errorMessageStr = ErrorPageMessages.Take(
            _controller.TempData, redirectResult.RouteValues?["m"]?.ToString()) ?? string.Empty;
        Assert.Contains($"{nonExistentId}", errorMessageStr);
    }
}
