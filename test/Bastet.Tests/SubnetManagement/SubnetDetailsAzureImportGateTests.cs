using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Services;
using Bastet.Services.Validation;
using Moq;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.SubnetManagement;

public class SubnetDetailsAzureImportGateTests : IDisposable
{
    public SubnetDetailsAzureImportGateTests() =>
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "true");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", null);
        GC.SuppressFinalize(this);
    }

    private const string VNetId =
        "/subscriptions/s/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-a";

    private static SubnetController CreateController(BastetDbContext context)
    {
        IIpUtilityService ip = new IpUtilityService();

        Mock<IUserContextService> user = new();
        user.Setup(m => m.GetCurrentUsername()).Returns("test-admin");
        user.Setup(m => m.UserHasRole(ApplicationRoles.Admin)).Returns(true);

        SubnetController controller = new(
            context,
            ip,
            new SubnetValidationService(ip),
            new HostIpValidationService(ip, context),
            user.Object,
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<SubnetController>.Instance);
        ControllerTestHelper.SetupController(controller);
        return controller;
    }

    private static async Task<bool> CanImport(
        bool hasChild, string? azureResourceId, bool isFullyAllocated = false, bool hasHostIp = false)
    {
        using BastetDbContext context = TestDbContextFactory.CreateDbContext();

        context.Subnets.Add(new Subnet
        {
            Id = 1,
            Name = "target",
            NetworkAddress = "10.20.0.0",
            Cidr = 16,
            IsFullyAllocated = isFullyAllocated,
            AzureResourceId = azureResourceId,
            CreatedAt = DateTime.UtcNow
        });

        if (hasChild)
        {
            context.Subnets.Add(new Subnet
            {
                Id = 2,
                Name = "child",
                NetworkAddress = "10.20.1.0",
                Cidr = 24,
                ParentSubnetId = 1,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (hasHostIp)
        {
            context.HostIpAssignments.Add(new HostIpAssignment
            {
                SubnetId = 1,
                IP = "10.20.0.5",
                Name = "host",
                CreatedAt = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        SubnetController controller = CreateController(context);
        _ = Assert.IsType<ViewResult>(await controller.Details(1));

        return (bool)controller.ViewBag.CanImportFromAzure!;
    }

    [Fact]
    public async Task APopulatedAzureLinkedTarget_ShowsTheImportLink() =>
        Assert.True(await CanImport(hasChild: true, azureResourceId: VNetId));

    [Fact]
    public async Task AnEmptyTarget_ShowsTheImportLink() =>
        Assert.True(await CanImport(hasChild: false, azureResourceId: null));

    [Fact]
    public async Task APopulatedTargetWithNoAzureLink_HidesTheImportLink() =>
        Assert.False(await CanImport(hasChild: true, azureResourceId: null));

    [Fact]
    public async Task AFullyAllocatedTarget_HidesTheImportLink() =>
        Assert.False(await CanImport(hasChild: false, azureResourceId: VNetId, isFullyAllocated: true));

    [Fact]
    public async Task ATargetWithHostIpAssignments_HidesTheImportLink() =>
        Assert.False(await CanImport(hasChild: false, azureResourceId: VNetId, hasHostIp: true));
}
