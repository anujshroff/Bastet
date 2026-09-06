using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;

namespace Bastet.Tests.Azure;

[Collection(AzureFeatureFlagCollection.Name)]
public class AzureControllerTests : IDisposable
{
    private readonly BastetDbContext _context;
    private readonly MockAzureService _mockAzureService;
    private readonly AzureController _controller;

    public AzureControllerTests()
    {

        DbContextOptions<BastetDbContext> options = new DbContextOptionsBuilder<BastetDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new BastetDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        _mockAzureService = new MockAzureService(true, CreateTestSubscriptions(), CreateTestVNets(), CreateTestSubnets());

        _controller = new AzureController(_mockAzureService, new AzureSubnetSnapshotService(_context), NullLogger<AzureController>.Instance)
        {

            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        _controller.HttpContext.Request.Headers.Referer = "https://localhost/Subnet/Details/2";

        _controller.TempData = new TempDataDictionary(
            _controller.HttpContext,
            Mock.Of<ITempDataProvider>());

        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "true");

        SeedTestData();
    }

    public void Dispose()
    {

        _context.Database.CloseConnection();
        _context.Dispose();

        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", null);

        GC.SuppressFinalize(this);
    }

    private void SeedTestData()
    {

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

        HostIpAssignment hostIp = new()
        {
            IP = "10.2.0.1",
            Name = "Test Host",
            SubnetId = 5,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-admin"
        };
        _context.HostIpAssignments.Add(hostIp);

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
            new() { Name = "subnet1", AddressPrefix = "10.0.0.0/24" },
            new() { Name = "subnet2", AddressPrefix = "10.0.1.0/24" }
        ];







    [Fact]
    public async Task GetSubscriptions_WithValidCredentials_ReturnsSubscriptions()
    {

        IActionResult result = await _controller.GetSubscriptions();

        JsonResult jsonResult = Assert.IsType<JsonResult>(result);
        Assert.NotNull(jsonResult.Value);

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

        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "false");

        IActionResult result = await _controller.GetSubscriptions();

        JsonResult jsonResult = Assert.IsType<JsonResult>(result);
        Assert.NotNull(jsonResult.Value);

        string json = JsonSerializer.Serialize(jsonResult.Value);
        JsonResponse? resultObj = JsonSerializer.Deserialize<JsonResponse>(json);

        Assert.NotNull(resultObj);
        Assert.False(resultObj.success);
        Assert.NotNull(resultObj.error);
        Assert.Contains("not enabled", resultObj.error);
    }

    [Fact]
    public async Task GetSubscriptions_WhenAzureThrows_DoesNotLeakTheExceptionMessage()
    {

        Mock<IAzureService> throwingService = new();
        throwingService.Setup(s => s.GetSubscriptions()).ThrowsAsync(new Exception("boom: secret detail"));
        AzureController controller = new(
            throwingService.Object, new AzureSubnetSnapshotService(_context), NullLogger<AzureController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        IActionResult result = await controller.GetSubscriptions();

        JsonResult jsonResult = Assert.IsType<JsonResult>(result);
        string json = JsonSerializer.Serialize(jsonResult.Value);
        JsonResponse? resultObj = JsonSerializer.Deserialize<JsonResponse>(json);

        Assert.NotNull(resultObj);
        Assert.False(resultObj.success);
        Assert.NotNull(resultObj.error);
        Assert.DoesNotContain("boom", resultObj.error);
        Assert.DoesNotContain("secret", resultObj.error);
    }

    [Fact]
    public async Task BulkGetVNets_AzureReadFails_ReportsFailureNotEmptySubscription()
    {

        AzureController controller = new(new MockAzureService(false), new AzureSubnetSnapshotService(_context), NullLogger<AzureController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        AzureBulkImportPlanner planner = new(new IpUtilityService(), new InputSanitizationService());

        IActionResult result = await controller.BulkGetVNets("sub-1", planner);

        JsonResult jsonResult = Assert.IsType<JsonResult>(result);
        string json = JsonSerializer.Serialize(jsonResult.Value);
        JsonResponse? resultObj = JsonSerializer.Deserialize<JsonResponse>(json);

        Assert.NotNull(resultObj);
        Assert.False(resultObj.success);
        Assert.False(string.IsNullOrEmpty(resultObj.error));
    }

    [Fact]
    public async Task BulkGetVNets_AzureReadSucceeds_ReturnsSuccess()
    {
        AzureBulkImportPlanner planner = new(new IpUtilityService(), new InputSanitizationService());

        IActionResult result = await _controller.BulkGetVNets("sub-1", planner);

        JsonResult jsonResult = Assert.IsType<JsonResult>(result);
        string json = JsonSerializer.Serialize(jsonResult.Value);
        JsonResponse? resultObj = JsonSerializer.Deserialize<JsonResponse>(json);

        Assert.NotNull(resultObj);
        Assert.True(resultObj.success);
        Assert.Null(resultObj.error);
    }

#pragma warning disable IDE1006

    private class JsonResponse
    {
        public bool success { get; set; }
        public string? error { get; set; }
        public List<AzureSubscriptionViewModel>? subscriptions { get; set; }
    }
#pragma warning restore IDE1006

}
