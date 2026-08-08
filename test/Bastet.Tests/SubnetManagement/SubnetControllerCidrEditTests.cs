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

        _context = TestDbContextFactory.CreateDbContext();

        _userContextService = ControllerTestHelper.CreateMockUserContextService();
        _ipUtilityService = new IpUtilityService();
        _validationService = new SubnetValidationService(_ipUtilityService);
        _sanitizationService = new InputSanitizationService();

        HostIpValidationService hostIpValidationService = new(_ipUtilityService, _context);

        _controller = new SubnetController(_context, _ipUtilityService, _validationService, hostIpValidationService, _userContextService, ControllerTestHelper.CreateMockSubnetLockingService(), NullLogger<SubnetController>.Instance);
        ControllerTestHelper.SetupController(_controller);

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

        _context.SaveChanges();
    }

    [Fact]
    public async Task Edit_GET_SubnetExists_ReturnsEditViewModel()
    {

        int subnetId = 4;

        IActionResult result = await _controller.Edit(subnetId);

        ViewResult viewResult = Assert.IsType<ViewResult>(result);
        EditSubnetViewModel model = Assert.IsType<EditSubnetViewModel>(viewResult.Model);

        Assert.Equal(subnetId, model.Id);
        Assert.Equal("10.0.2.0", model.NetworkAddress);
        Assert.Equal(24, model.Cidr);
        Assert.Equal(24, model.OriginalCidr);

        string? parentInfo = model.ParentSubnetInfo;
        Assert.NotNull(parentInfo);
        Assert.Contains("10.0.0.0/16", parentInfo);
    }

    [Fact]
    public async Task Edit_GET_NonExistentSubnet_RedirectsToNotFoundError()
    {

        int nonExistentId = 999;

        IActionResult result = await _controller.Edit(nonExistentId);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("HttpStatusCodeHandler", redirectResult.ActionName);
        Assert.Equal("Error", redirectResult.ControllerName);

        object? statusCode = redirectResult.RouteValues?["statusCode"];
        Assert.NotNull(statusCode);
        Assert.Equal(404, statusCode);

        string errorMessageStr = ErrorPageMessages.Take(
            _controller.TempData, redirectResult.RouteValues?["m"]?.ToString()) ?? string.Empty;
        Assert.Contains($"{nonExistentId}", errorMessageStr);
    }

    [Fact]
    public async Task Edit_POST_NoChanges_ReturnsRedirectToDetails()
    {

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "Target Subnet (10.0.2.0/24)",
            NetworkAddress = "10.0.2.0",
            Cidr = 24,
            OriginalCidr = 24
        };

        IActionResult result = await _controller.Edit(4, viewModel);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);

        object? idValue = redirectResult.RouteValues?["id"];
        Assert.NotNull(idValue);
        Assert.Equal(4, idValue);

        string? successMessage = _controller.TempData["SuccessMessage"]?.ToString();
        Assert.NotNull(successMessage);
        Assert.Contains("was updated successfully", successMessage);
    }

    [Fact]
    public async Task Edit_POST_UpdateNameAndDescription_ReturnsRedirectToDetails()
    {

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "Updated Name",
            NetworkAddress = "10.0.2.0",
            Cidr = 24,
            OriginalCidr = 24,
            Description = "Updated description"
        };

        IActionResult result = await _controller.Edit(4, viewModel);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);

        Subnet? updatedSubnet = await _context.Subnets.FindAsync([4], TestContext.Current.CancellationToken);
        Assert.NotNull(updatedSubnet);

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

        _context.Subnets.Remove(await _context.Subnets.FindAsync([5], TestContext.Current.CancellationToken) ?? throw new Exception("Child 1 not found"));
        _context.Subnets.Remove(await _context.Subnets.FindAsync([6], TestContext.Current.CancellationToken) ?? throw new Exception("Child 2 not found"));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "Target Subnet",
            NetworkAddress = "10.0.2.0",
            Cidr = 25,
            OriginalCidr = 24
        };

        IActionResult result = await _controller.Edit(4, viewModel);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);

        Subnet? updatedSubnet = await _context.Subnets.FindAsync([4], TestContext.Current.CancellationToken);
        Assert.NotNull(updatedSubnet);
        int cidr = updatedSubnet.Cidr;
        Assert.Equal(25, cidr);
    }

    [Fact]
    public async Task Edit_POST_DecreaseCidr_NoConflicts_ReturnsRedirectToDetails()
    {

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
            Cidr = 23,
            OriginalCidr = 24
        };

        IActionResult result = await _controller.Edit(10, viewModel);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);

        Subnet? updatedSubnet = await _context.Subnets.FindAsync([10], TestContext.Current.CancellationToken);
        Assert.NotNull(updatedSubnet);
        int cidr = updatedSubnet.Cidr;
        Assert.Equal(23, cidr);
    }

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

        _ = Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);

        Subnet? unchanged = await _context.Subnets.FindAsync([20], TestContext.Current.CancellationToken);
        Assert.NotNull(unchanged);
        Assert.Equal(31, unchanged.Cidr);
    }

    [Fact]
    public async Task Edit_POST_DecreaseCidr_WithGrandparentAndGrandchild_ReturnsRedirectToDetails()
    {

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
            ParentSubnetId = 5,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(grandchild);

        Subnet parent = (await _context.Subnets.FindAsync([1], TestContext.Current.CancellationToken))!;
        parent.ParentSubnetId = 11;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

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

    [Fact]
    public async Task Edit_POST_MissingName_ReturnsViewWithError()
    {

        _controller.ModelState.AddModelError("Name", "Name is required");

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "",
            NetworkAddress = "10.0.2.0",
            Cidr = 24,
            OriginalCidr = 24
        };

        IActionResult result = await _controller.Edit(4, viewModel);

        _ = Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);
        Assert.Contains("Name", _controller.ModelState.Keys);
    }

    [Fact]
    public async Task Edit_POST_InvalidCidr_ReturnsViewWithError()
    {

        _controller.ModelState.AddModelError("Cidr", "CIDR must be between 0 and 32");

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "Target Subnet",
            NetworkAddress = "10.0.2.0",
            Cidr = 24,
            OriginalCidr = 24
        };

        IActionResult result = await _controller.Edit(4, viewModel);

        _ = Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);
        Assert.Contains("Cidr", _controller.ModelState.Keys);
    }

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

        Assert.Equal(cidr, viewModel.Cidr);
    }

    [Fact]
    public async Task Edit_POST_IncreaseCidr_OrphansChildren_ReturnsViewWithError()
    {

        _controller.ModelState.AddModelError("Cidr", "Child subnet Child 2 (10.0.2.128/25) would no longer fit within this subnet if CIDR is increased to /25");

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "Target Subnet",
            NetworkAddress = "10.0.2.0",
            Cidr = 25,
            OriginalCidr = 24
        };

        IActionResult result = await _controller.Edit(4, viewModel);

        ViewResult viewResult = Assert.IsType<ViewResult>(result);
        _ = Assert.IsType<EditSubnetViewModel>(viewResult.Model);

        Assert.False(_controller.ModelState.IsValid);
        Assert.Contains("Cidr", _controller.ModelState.Keys);

        Microsoft.AspNetCore.Mvc.ModelBinding.ModelErrorCollection? cidrErrors = _controller.ModelState["Cidr"]?.Errors;
        Assert.NotNull(cidrErrors);
        Assert.NotEmpty(cidrErrors);
        Assert.Contains("Child subnet", cidrErrors.First().ErrorMessage ?? string.Empty);
    }

    [Fact]
    public async Task Edit_POST_DecreaseCidr_BeyondParent_ReturnsViewWithError()
    {

        _controller.ModelState.AddModelError("Cidr", "Decreasing CIDR to /15 would make this subnet too large to fit within its parent subnet (10.0.0.0/16)");

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "Target Subnet",
            NetworkAddress = "10.0.2.0",
            Cidr = 15,
            OriginalCidr = 24
        };

        IActionResult result = await _controller.Edit(4, viewModel);

        _ = Assert.IsType<ViewResult>(result);

        Assert.False(_controller.ModelState.IsValid);
        Assert.Contains("Cidr", _controller.ModelState.Keys);

        Microsoft.AspNetCore.Mvc.ModelBinding.ModelErrorCollection? cidrErrors = _controller.ModelState["Cidr"]?.Errors;
        Assert.NotNull(cidrErrors);
        Assert.NotEmpty(cidrErrors);
        Assert.Contains("parent subnet", cidrErrors.First().ErrorMessage?.ToLower() ?? string.Empty);
    }

    [Fact]
    public async Task Edit_POST_MisalignedNetworkAddress_ReturnsViewWithError()
    {

        _controller.ModelState.AddModelError("NetworkAddress", "Network address is not valid for the given CIDR. The network address must align with the subnet boundary.");

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "Target Subnet",
            NetworkAddress = "10.0.2.1",
            Cidr = 24,
            OriginalCidr = 24
        };

        IActionResult result = await _controller.Edit(4, viewModel);

        _ = Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);
        Assert.Contains("NetworkAddress", _controller.ModelState.Keys);

        Microsoft.AspNetCore.Mvc.ModelBinding.ModelErrorCollection? networkAddressErrors = _controller.ModelState["NetworkAddress"]?.Errors;
        Assert.NotNull(networkAddressErrors);
        Assert.NotEmpty(networkAddressErrors);
        Assert.Contains("subnet boundary", networkAddressErrors.First().ErrorMessage?.ToLower() ?? string.Empty);
    }

    [Fact]
    public async Task Edit_POST_DecreaseCidr_OverlapsWithUnrelatedSubnet_ReturnsViewWithError()
    {

        Subnet unrelatedOverlapSubnet = new()
        {
            Id = 20,
            Name = "Unrelated Subnet",
            NetworkAddress = "10.0.3.0",
            Cidr = 24,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(unrelatedOverlapSubnet);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        _controller.ModelState.AddModelError("Cidr", "Expanding to 10.0.2.0/22 would conflict with existing subnet: Unrelated Subnet (10.0.3.0/24)");

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "Target Subnet",
            NetworkAddress = "10.0.2.0",
            Cidr = 22,
            OriginalCidr = 24
        };

        IActionResult result = await _controller.Edit(4, viewModel);

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

        _context.Subnets.Remove(await _context.Subnets.FindAsync([5], TestContext.Current.CancellationToken) ?? throw new Exception("Child 1 not found"));
        _context.Subnets.Remove(await _context.Subnets.FindAsync([6], TestContext.Current.CancellationToken) ?? throw new Exception("Child 2 not found"));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

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

        Subnet? targetSubnet = await _context.Subnets.FindAsync([4], TestContext.Current.CancellationToken) ?? throw new Exception("Target subnet not found");
        targetSubnet.Cidr = 23;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "Target Subnet",
            NetworkAddress = "10.0.2.0",
            Cidr = 24,
            OriginalCidr = 23
        };

        IActionResult result = await _controller.Edit(4, viewModel);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);

        Subnet? updatedSubnet = await _context.Subnets.FindAsync([4], TestContext.Current.CancellationToken);
        Assert.NotNull(updatedSubnet);
        int cidr = updatedSubnet.Cidr;
        Assert.Equal(24, cidr);
    }

    [Fact]
    public async Task Edit_POST_MultipleValidationErrors_ReturnsViewWithAllErrors()
    {

        _controller.ModelState.AddModelError("Name", "Name is required");
        _controller.ModelState.AddModelError("Cidr", "CIDR must be between 0 and 32");
        _controller.ModelState.AddModelError("Description", "Description cannot be longer than 1000 characters");

        EditSubnetViewModel viewModel = new()
        {
            Id = 4,
            Name = "",
            NetworkAddress = "10.0.2.0",
            Cidr = 24,
            OriginalCidr = 24,

            Description = new string('x', 1100)
        };

        IActionResult result = await _controller.Edit(4, viewModel);

        _ = Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);
        Assert.Equal(3, _controller.ModelState.ErrorCount);
        Assert.Contains("Name", _controller.ModelState.Keys);
        Assert.Contains("Cidr", _controller.ModelState.Keys);
        Assert.Contains("Description", _controller.ModelState.Keys);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(15)]
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
            Cidr = 16,
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

        int nonExistentId = 999;
        EditSubnetViewModel viewModel = new()
        {
            Id = nonExistentId,
            Name = "Non-existent Subnet",
            NetworkAddress = "10.1.1.0",
            Cidr = 24,
            OriginalCidr = 24
        };

        IActionResult result = await _controller.Edit(nonExistentId, viewModel);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("HttpStatusCodeHandler", redirectResult.ActionName);
        Assert.Equal("Error", redirectResult.ControllerName);

        object? statusCode = redirectResult.RouteValues?["statusCode"];
        Assert.NotNull(statusCode);
        Assert.Equal(404, statusCode);

        string errorMessageStr = ErrorPageMessages.Take(
            _controller.TempData, redirectResult.RouteValues?["m"]?.ToString()) ?? string.Empty;
        Assert.Contains($"{nonExistentId}", errorMessageStr);
    }
}
