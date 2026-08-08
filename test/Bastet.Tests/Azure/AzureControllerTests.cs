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

        _controller = new AzureController(_context, _mockAzureService, new AzureSubnetSnapshotService(_context), new IpUtilityService(), NullLogger<AzureController>.Instance)
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
            new() { Name = "subnet1", AddressPrefix = "10.0.0.0/24", HasMultipleAddressSchemes = false },
            new() { Name = "subnet2", AddressPrefix = "10.0.1.0/24", HasMultipleAddressSchemes = false }
        ];

    [Fact]
    public async Task Import_GET_ValidSubnet_ReturnsImportViewModel()
    {

        int subnetId = 2;

        IActionResult result = await _controller.Import(subnetId);

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

        int subnetId = 3;

        IActionResult result = await _controller.Import(subnetId);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal("Subnet", redirectResult.ControllerName);

        string? errorMessage = _controller.TempData["ErrorMessage"]?.ToString();
        Assert.NotNull(errorMessage);
        Assert.Contains("child subnets", errorMessage);
    }

    [Fact]
    public async Task Import_GET_SubnetWithHostIps_RedirectsToDetails()
    {

        int subnetId = 5;

        IActionResult result = await _controller.Import(subnetId);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        Assert.Equal("Subnet", redirectResult.ControllerName);

        string? errorMessage = _controller.TempData["ErrorMessage"]?.ToString();
        Assert.NotNull(errorMessage);
        Assert.Contains("host IP", errorMessage);
    }

    [Fact]
    public async Task Import_GET_NonExistentSubnet_RedirectsToNotFoundError()
    {

        int nonExistentId = 999;

        IActionResult result = await _controller.Import(nonExistentId);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("HttpStatusCodeHandler", redirectResult.ActionName);
        Assert.Equal("Error", redirectResult.ControllerName);

        object? statusCode = redirectResult.RouteValues?["statusCode"];
        Assert.NotNull(statusCode);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task Import_GET_FeatureFlagDisabled_RedirectsToForbiddenError()
    {

        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "false");

        IActionResult result = await _controller.Import(2);

        RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("HttpStatusCodeHandler", redirectResult.ActionName);
        Assert.Equal("Error", redirectResult.ControllerName);

        object? statusCode = redirectResult.RouteValues?["statusCode"];
        Assert.NotNull(statusCode);
        Assert.Equal(403, statusCode);
    }

    [Fact]
    public async Task Import_GET_InvalidAzureCredentials_AddsModelError()
    {

        int subnetId = 2;
        AzureController controller = new(_context, new MockAzureService(false), new AzureSubnetSnapshotService(_context), new IpUtilityService(), NullLogger<AzureController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        IActionResult result = await controller.Import(subnetId);

        ViewResult viewResult = Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.True(controller.ModelState.ErrorCount > 0);
        Assert.Contains(controller.ModelState.Values, v => v.Errors.Any(e => e.ErrorMessage.Contains("authenticate")));
    }

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
            _context, throwingService.Object, new AzureSubnetSnapshotService(_context), new IpUtilityService(), NullLogger<AzureController>.Instance)
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

    private AzureController ControllerWith(IAzureService service) =>
        new(_context, service, new AzureSubnetSnapshotService(_context), new IpUtilityService(), NullLogger<AzureController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static JsonResponse Parse(IActionResult result)
    {
        JsonResult jsonResult = Assert.IsType<JsonResult>(result);
        JsonResponse? parsed = JsonSerializer.Deserialize<JsonResponse>(JsonSerializer.Serialize(jsonResult.Value));
        Assert.NotNull(parsed);
        return parsed;
    }

    [Fact]
    public async Task GetVNets_WhenAzureThrows_ReportsFailureRatherThanNoVNets()
    {
        Mock<IAzureService> throwing = new();
        throwing.Setup(s => s.GetCompatibleVNets(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ThrowsAsync(new Exception("429 throttled: secret detail"));

        JsonResponse response = Parse(await ControllerWith(throwing.Object).GetVNets("sub-1", 2));

        Assert.False(response.success);
        Assert.NotNull(response.error);
        Assert.DoesNotContain("secret", response.error);
    }

    [Fact]
    public async Task GetSubnets_WhenAzureThrows_ReportsFailureRatherThanNoSubnets()
    {
        Mock<IAzureService> throwing = new();
        throwing.Setup(s => s.GetCompatibleSubnets(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ThrowsAsync(new Exception("403 AuthorizationFailed: secret detail"));

        JsonResponse response = Parse(await ControllerWith(throwing.Object).GetSubnets("/vnet/id", 2));

        Assert.False(response.success);
        Assert.NotNull(response.error);
        Assert.DoesNotContain("secret", response.error);
    }

    [Fact]
    public async Task GetVNets_WhenAzureGenuinelyHasNone_IsStillSuccess()
    {
        Mock<IAzureService> empty = new();
        empty.Setup(s => s.GetCompatibleVNets(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync([]);

        JsonResponse response = Parse(await ControllerWith(empty.Object).GetVNets("sub-1", 2));

        Assert.True(response.success);
    }

    [Fact]
    public async Task GetVNets_WithValidParams_ReturnsVNets()
    {

        string subscriptionId = "sub-1";
        int subnetId = 2;

        IActionResult result = await _controller.GetVNets(subscriptionId, subnetId);

        JsonResult jsonResult = Assert.IsType<JsonResult>(result);
        Assert.NotNull(jsonResult.Value);

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

        string vnetResourceId = "/subscriptions/sub-1/resourceGroups/test-rg/providers/Microsoft.Network/virtualNetworks/vnet1";
        int subnetId = 2;

        IActionResult result = await _controller.GetSubnets(vnetResourceId, subnetId);

        JsonResult jsonResult = Assert.IsType<JsonResult>(result);
        Assert.NotNull(jsonResult.Value);

        string json = JsonSerializer.Serialize(jsonResult.Value);
        JsonResponse? resultObj = JsonSerializer.Deserialize<JsonResponse>(json);

        Assert.NotNull(resultObj);
        Assert.True(resultObj.success);
        Assert.NotNull(resultObj.subnets);
        Assert.Equal(2, resultObj.subnets.Count);
        Assert.Contains(resultObj.subnets, s => s.Name == "subnet1");
        Assert.Contains(resultObj.subnets, s => s.Name == "subnet2");
    }

    [Fact]
    public async Task GetSubnets_TheRowEncompassingTheTargetPrefix_SurvivesTheAlreadyRecordedFilter()
    {
        const string VNetId = "/subscriptions/sub-1/resourceGroups/test-rg/providers/Microsoft.Network/virtualNetworks/vnet-enc";

        List<AzureVNetViewModel> vnets =
            [new() { ResourceId = VNetId, Name = "vnet-enc", AddressPrefixes = ["10.171.0.0/24"] }];

        List<AzureSubnetViewModel> subnets =
        [
            new() { Name = "snet-full", AddressPrefix = "10.171.0.0/24", HasMultipleAddressSchemes = false },
            new() { Name = "snet-child", AddressPrefix = "10.171.0.0/25", HasMultipleAddressSchemes = false }
        ];

        _context.Subnets.Add(new Subnet { Id = 90, Name = "target", NetworkAddress = "10.171.0.0", Cidr = 24 });
        _context.Subnets.Add(new Subnet { Id = 91, Name = "child", NetworkAddress = "10.171.0.0", Cidr = 25, ParentSubnetId = 90 });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        AzureController controller = new(
            _context,
            new MockAzureService(true, CreateTestSubscriptions(), vnets, subnets),
            new AzureSubnetSnapshotService(_context),
            new IpUtilityService(),
            NullLogger<AzureController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        JsonResult json = Assert.IsType<JsonResult>(await controller.GetSubnets(VNetId, 90));
        JsonResponse? response = JsonSerializer.Deserialize<JsonResponse>(JsonSerializer.Serialize(json.Value));

        Assert.NotNull(response);
        Assert.True(response.success);
        Assert.NotNull(response.subnets);

        AzureSubnetViewModel full = Assert.Single(response.subnets, s => s.Name == "snet-full");
        AzureSubnetViewModel child = Assert.Single(response.subnets, s => s.Name == "snet-child");

        Assert.False(full.IsSelectable);
        Assert.Contains("already has child subnets", full.Reason);

        Assert.False(child.IsSelectable);
        Assert.Contains("already uses", child.Reason);
    }

    [Fact]
    public async Task GetSubnets_TheEncompassingRow_StaysSelectableWhenTheTargetIsEmpty()
    {
        const string VNetId = "/subscriptions/sub-1/resourceGroups/test-rg/providers/Microsoft.Network/virtualNetworks/vnet-enc2";

        List<AzureVNetViewModel> vnets =
            [new() { ResourceId = VNetId, Name = "vnet-enc2", AddressPrefixes = ["10.172.0.0/24"] }];
        List<AzureSubnetViewModel> subnets =
            [new() { Name = "snet-full", AddressPrefix = "10.172.0.0/24", HasMultipleAddressSchemes = false }];

        _context.Subnets.Add(new Subnet { Id = 92, Name = "empty-target", NetworkAddress = "10.172.0.0", Cidr = 24 });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        JsonResult json = Assert.IsType<JsonResult>(
            await ControllerWithSubnets(vnets, subnets).GetSubnets(VNetId, 92));
        JsonResponse? response = JsonSerializer.Deserialize<JsonResponse>(JsonSerializer.Serialize(json.Value));

        AzureSubnetViewModel full = Assert.Single(response!.subnets!, s => s.Name == "snet-full");
        Assert.True(full.IsSelectable);
        Assert.Null(full.Reason);
    }

    [Fact]
    public async Task GetSubnets_ARowThatWouldContainAnExistingSubnet_IsBlockedWithAReason()
    {
        const string VNetId = "/subscriptions/sub-1/resourceGroups/test-rg/providers/Microsoft.Network/virtualNetworks/vnet-wc";

        List<AzureVNetViewModel> vnets =
            [new() { ResourceId = VNetId, Name = "vnet-wc", AddressPrefixes = ["10.94.0.0/16"] }];
        List<AzureSubnetViewModel> subnets =
        [
            new() { Name = "s2", AddressPrefix = "10.94.2.0/24" },
            new() { Name = "s3", AddressPrefix = "10.94.3.0/24" }
        ];

        _context.Subnets.Add(new Subnet { Id = 93, Name = "target", NetworkAddress = "10.94.0.0", Cidr = 16 });
        _context.Subnets.Add(new Subnet { Id = 94, Name = "hand-2-128", NetworkAddress = "10.94.2.128", Cidr = 25, ParentSubnetId = 93 });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        JsonResult json = Assert.IsType<JsonResult>(
            await ControllerWithSubnets(vnets, subnets).GetSubnets(VNetId, 93));
        JsonResponse? response = JsonSerializer.Deserialize<JsonResponse>(JsonSerializer.Serialize(json.Value));

        AzureSubnetViewModel s2 = Assert.Single(response!.subnets!, s => s.Name == "s2");
        Assert.False(s2.IsSelectable);
        Assert.Contains("hand-2-128", s2.Reason);

        AzureSubnetViewModel s3 = Assert.Single(response.subnets!, s => s.Name == "s3");
        Assert.True(s3.IsSelectable);
    }

    [Fact]
    public async Task GetSubnets_ARowWithAMoreSpecificExistingParent_IsBlockedWithAReason()
    {
        const string VNetId = "/subscriptions/sub-1/resourceGroups/test-rg/providers/Microsoft.Network/virtualNetworks/vnet-msp";

        List<AzureVNetViewModel> vnets =
            [new() { ResourceId = VNetId, Name = "vnet-msp", AddressPrefixes = ["10.95.0.0/16"] }];
        List<AzureSubnetViewModel> subnets =
            [new() { Name = "s4", AddressPrefix = "10.95.4.0/24" }];

        _context.Subnets.Add(new Subnet { Id = 95, Name = "target", NetworkAddress = "10.95.0.0", Cidr = 16 });
        _context.Subnets.Add(new Subnet { Id = 96, Name = "hand-4-23", NetworkAddress = "10.95.4.0", Cidr = 23, ParentSubnetId = 95 });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        JsonResult json = Assert.IsType<JsonResult>(
            await ControllerWithSubnets(vnets, subnets).GetSubnets(VNetId, 95));
        JsonResponse? response = JsonSerializer.Deserialize<JsonResponse>(JsonSerializer.Serialize(json.Value));

        AzureSubnetViewModel s4 = Assert.Single(response!.subnets!, s => s.Name == "s4");
        Assert.False(s4.IsSelectable);
        Assert.Contains("hand-4-23", s4.Reason);
    }

    private AzureController ControllerWithSubnets(
        List<AzureVNetViewModel> vnets, List<AzureSubnetViewModel> subnets) =>
        new(_context,
            new MockAzureService(true, CreateTestSubscriptions(), vnets, subnets),
            new AzureSubnetSnapshotService(_context),
            new IpUtilityService(),
            NullLogger<AzureController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    [Fact]
    public async Task BulkGetVNets_AzureReadFails_ReportsFailureNotEmptySubscription()
    {

        AzureController controller = new(_context, new MockAzureService(false), new AzureSubnetSnapshotService(_context), new IpUtilityService(), NullLogger<AzureController>.Instance)
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
        public List<AzureVNetViewModel>? vnets { get; set; }
        public List<AzureSubnetViewModel>? subnets { get; set; }
    }
#pragma warning restore IDE1006
}
