using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Text.Json;

namespace Bastet.Tests.Azure;

/// <summary>
/// Integration tests for the AzureController
/// </summary>
public class AzureControllerTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly MockAzureService _mockAzureService;
    private readonly AzureController _controller;

    public AzureControllerTests()
    {
        // Use SQLite in-memory database for tests
        DbContextOptions<BastetDbContext> options = new DbContextOptionsBuilder<BastetDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new BastetDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        // Set up mock Azure service
        _mockAzureService = new MockAzureService(true, CreateTestSubscriptions(), CreateTestVNets(), CreateTestSubnets());

        // Create and configure the controller
        _controller = new AzureController(_context, _mockAzureService)
        {
            // Setup controller context with HttpContext
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        // Add Referer header for testing
        _controller.HttpContext.Request.Headers.Referer = "https://localhost/Subnet/Details/2";

        // Setup TempData for controller (required to avoid NullReferenceException)
        _controller.TempData = new TempDataDictionary(
            _controller.HttpContext,
            Mock.Of<ITempDataProvider>());

        // Setup environment variable for feature flag
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "true");

        // Set up test data
        SeedTestData();
    }

    public void Dispose()
    {
        // Cleanup resources
        _context.Database.CloseConnection();
        _context.Dispose();

        // Reset environment variables
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", null);

        GC.SuppressFinalize(this);
    }

    private void SeedTestData()
    {
        // Create test subnets

        // Root subnet - no parent
        Subnet rootSubnet = new()
        {
            Id = 1,
            Name = "Root Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 8,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(rootSubnet);

        // Parent subnet - for import testing
        Subnet parentSubnet = new()
        {
            Id = 2,
            Name = "Parent Subnet",
            NetworkAddress = "10.0.0.0",
            Cidr = 16,
            ParentSubnetId = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(parentSubnet);

        // A subnet with child subnets - should be ineligible for import
        Subnet subnetWithChildren = new()
        {
            Id = 3,
            Name = "Subnet With Children",
            NetworkAddress = "10.1.0.0",
            Cidr = 16,
            ParentSubnetId = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(subnetWithChildren);

        // Child subnet of subnetWithChildren
        Subnet childSubnet = new()
        {
            Id = 4,
            Name = "Child Subnet",
            NetworkAddress = "10.1.0.0",
            Cidr = 24,
            ParentSubnetId = 3,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(childSubnet);

        // A subnet with host IPs - should be ineligible for import
        Subnet subnetWithHostIps = new()
        {
            Id = 5,
            Name = "Subnet With Host IPs",
            NetworkAddress = "10.2.0.0",
            Cidr = 16,
            ParentSubnetId = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.Subnets.Add(subnetWithHostIps);

        // Host IP for subnetWithHostIps
        HostIpAssignment hostIp = new()
        {
            IP = "10.2.0.1",
            Name = "Test Host",
            SubnetId = 5,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.HostIpAssignments.Add(hostIp);

        // Save all changes
        _context.SaveChanges();
    }

    private static List<AzureSubscriptionViewModel> CreateTestSubscriptions() => [
            new() { SubscriptionId = "sub-1", DisplayName = "Test Subscription 1" },
            new() { SubscriptionId = "sub-2", DisplayName = "Test Subscription 2" }
        ];

    private static List<AzureVNetViewModel> CreateTestVNets() => [
            new()
            {
                ResourceId = "/subscriptions/sub-1/resourceGroups/test-rg/providers/Microsoft.Network/virtualNetworks/vnet1",
                Name = "vnet1",
                AddressPrefixes = ["10.0.0.0/16"]
            },
            new()
            {
                ResourceId = "/subscriptions/sub-1/resourceGroups/test-rg/providers/Microsoft.Network/virtualNetworks/vnet2",
                Name = "vnet2",
                AddressPrefixes = ["172.16.0.0/12"]
            }
        ];

    private static List<AzureSubnetViewModel> CreateTestSubnets() => [
            new() { Name = "subnet1", AddressPrefix = "10.0.0.0/24", HasMultipleAddressSchemes = false },
            new() { Name = "subnet2", AddressPrefix = "10.0.1.0/24", HasMultipleAddressSchemes = false }
        ];

    [Fact]
    public async Task Import_GET_ValidSubnet_ReturnsImportViewModel()
    {
        // Arrange
        int subnetId = 2; // Parent Subnet - eligible for import

        // Act
        IActionResult result = await _controller.Import(subnetId);

        // Assert
        ViewResult viewResult = Assert.IsType<ViewResult>(result);
        AzureImportViewModel model = Assert.IsType<AzureImportViewModel>(viewResult.Model);

        Assert.Equal(subnetId, model.SubnetId);
        Assert.Equal("Parent Subnet", model.SubnetName);
        Assert.Equal("10.0.0.0", model.NetworkAddress);
        Assert.Equal(16, model.Cidr);
    }

    [Fact]
    public async Task Import_GET_SubnetWithChildren_RedirectsToDetails()
    {
        // Arrange
        int subnetId = 3; // Subnet with children - ineligible for import

        // Act
        IActionResult result = await _controller.Import(subnetId);

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal("Subnet", redirectResult.ControllerName);

        // Check for error message
        string? errorMessage = _controller.TempData["ErrorMessage"]?.ToString();
        Assert.NotNull(errorMessage);
        Assert.Contains("child subnets", errorMessage);
    }

    [Fact]
    public async Task Import_GET_SubnetWithHostIps_RedirectsToDetails()
    {
        // Arrange
        int subnetId = 5; // Subnet with host IPs - ineligible for import

        // Act
        IActionResult result = await _controller.Import(subnetId);

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal("Subnet", redirectResult.ControllerName);

        // Check for error message
        string? errorMessage = _controller.TempData["ErrorMessage"]?.ToString();
        Assert.NotNull(errorMessage);
        Assert.Contains("host IP", errorMessage);
    }

    [Fact]
    public async Task Import_GET_NonExistentSubnet_RedirectsToNotFoundError()
    {
        // Arrange
        int nonExistentId = 999;

        // Act
        IActionResult result = await _controller.Import(nonExistentId);

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("HttpStatusCodeHandler", redirectResult.ActionName);
        Assert.Equal("Error", redirectResult.ControllerName);

        // Check for 404 status code
        object? statusCode = redirectResult.RouteValues?["statusCode"];
        Assert.NotNull(statusCode);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task Import_GET_FeatureFlagDisabled_RedirectsToForbiddenError()
    {
        // Arrange
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "false");

        // Act
        IActionResult result = await _controller.Import(2);

        // Assert
        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("HttpStatusCodeHandler", redirectResult.ActionName);
        Assert.Equal("Error", redirectResult.ControllerName);

        // Check for 403 status code
        object? statusCode = redirectResult.RouteValues?["statusCode"];
        Assert.NotNull(statusCode);
        Assert.Equal(403, statusCode);
    }

    [Fact]
    public async Task Import_GET_InvalidAzureCredentials_AddsModelError()
    {
        // Arrange
        int subnetId = 2;
        AzureController controller = new(_context, new MockAzureService(false))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        // Act
        IActionResult result = await controller.Import(subnetId);

        // Assert
        ViewResult viewResult = Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.True(controller.ModelState.ErrorCount > 0);
        Assert.Contains(controller.ModelState.Values, v => v.Errors.Any(e => e.ErrorMessage.Contains("authenticate")));
    }

    [Fact]
    public async Task GetSubscriptions_WithValidCredentials_ReturnsSubscriptions()
    {
        // Act
        IActionResult result = await _controller.GetSubscriptions();

        // Assert
        JsonResult jsonResult = Assert.IsType<JsonResult>(result);
        Assert.NotNull(jsonResult.Value);

        // Since we can't directly access the anonymous type properties,
        // convert to string and then deserialize to test content
        string json = JsonSerializer.Serialize(jsonResult.Value);
        JsonResponse? resultObj = JsonSerializer.Deserialize<JsonResponse>(json);

        Assert.NotNull(resultObj);
        Assert.True(resultObj.success);
        Assert.NotNull(resultObj.subscriptions);
        Assert.Equal(2, resultObj.subscriptions.Count);
        Assert.Contains(resultObj.subscriptions, s => s.SubscriptionId == "sub-1");
    }

    [Fact]
    public async Task GetSubscriptions_WithFeatureFlagDisabled_ReturnsError()
    {
        // Arrange
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "false");

        // Act
        IActionResult result = await _controller.GetSubscriptions();

        // Assert
        JsonResult jsonResult = Assert.IsType<JsonResult>(result);
        Assert.NotNull(jsonResult.Value);

        // Convert to string and then deserialize to test content
        string json = JsonSerializer.Serialize(jsonResult.Value);
        JsonResponse? resultObj = JsonSerializer.Deserialize<JsonResponse>(json);

        Assert.NotNull(resultObj);
        Assert.False(resultObj.success);
        Assert.NotNull(resultObj.error);
        Assert.Contains("not enabled", resultObj.error);
    }

    [Fact]
    public async Task GetVNets_WithValidParams_ReturnsVNets()
    {
        // Arrange
        string subscriptionId = "sub-1";
        int subnetId = 2;

        // Act
        IActionResult result = await _controller.GetVNets(subscriptionId, subnetId);

        // Assert
        JsonResult jsonResult = Assert.IsType<JsonResult>(result);
        Assert.NotNull(jsonResult.Value);

        // Convert to string and then deserialize to test content
        string json = JsonSerializer.Serialize(jsonResult.Value);
        JsonResponse? resultObj = JsonSerializer.Deserialize<JsonResponse>(json);

        Assert.NotNull(resultObj);
        Assert.True(resultObj.success);
        Assert.Equal(1, resultObj.vnets?.Count);
        Assert.Equal("vnet1", resultObj.vnets?[0].Name);
    }

    [Fact]
    public async Task GetSubnets_WithValidParams_ReturnsSubnets()
    {
        // Arrange
        string vnetResourceId = "/subscriptions/sub-1/resourceGroups/test-rg/providers/Microsoft.Network/virtualNetworks/vnet1";
        int subnetId = 2;

        // Act
        IActionResult result = await _controller.GetSubnets(vnetResourceId, subnetId);

        // Assert
        JsonResult jsonResult = Assert.IsType<JsonResult>(result);
        Assert.NotNull(jsonResult.Value);

        // Convert to string and then deserialize to test content
        string json = JsonSerializer.Serialize(jsonResult.Value);
        JsonResponse? resultObj = JsonSerializer.Deserialize<JsonResponse>(json);

        Assert.NotNull(resultObj);
        Assert.True(resultObj.success);
        Assert.NotNull(resultObj.subnets);
        Assert.Equal(2, resultObj.subnets.Count);
        Assert.Contains(resultObj.subnets, s => s.Name == "subnet1");
        Assert.Contains(resultObj.subnets, s => s.Name == "subnet2");
    }

#pragma warning disable IDE1006 // Naming Styles
    // Helper class for deserializing JSON responses
    private class JsonResponse
    {
        public bool success { get; set; }
        public string? error { get; set; }
        public List<AzureSubscriptionViewModel>? subscriptions { get; set; }
        public List<AzureVNetViewModel>? vnets { get; set; }
        public List<AzureSubnetViewModel>? subnets { get; set; }
    }
#pragma warning restore IDE1006 // Naming Styles
}
