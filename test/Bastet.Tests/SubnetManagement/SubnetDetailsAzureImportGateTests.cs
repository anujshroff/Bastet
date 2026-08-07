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

/// <summary>
/// O9. The "can this subnet be imported from Azure" predicate exists in two places: the authority
/// in <c>AzureController.Import</c>, and <c>ViewBag.CanImportFromAzure</c> on the Details page,
/// which gates the only link in the whole application that reaches <c>/Azure/Import/{id}</c>.
/// Round 14 relaxed the authority to admit a top-up and did not touch the copy, so the two became
/// mutually exclusive: whenever the server would accept a top-up, the button that leads to it was
/// not rendered, and it disappeared permanently after the first successful single-VNet import.
///
/// No test referenced <c>CanImportFromAzure</c> at all, which is why that slipped past. These pin
/// the two predicates as set-equivalent on every arm.
/// </summary>
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

        // The gate under test has an Admin conjunct, so the mock has to actually be one.
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

    /// <summary>The steady state of the feature, and the defect: a populated Azure-linked target is
    /// exactly what the server admits for a top-up, and the link was not rendered.</summary>
    [Fact]
    public async Task APopulatedAzureLinkedTarget_ShowsTheImportLink() =>
        Assert.True(await CanImport(hasChild: true, azureResourceId: VNetId));

    /// <summary>An empty target is the ordinary first import and was never in question.</summary>
    [Fact]
    public async Task AnEmptyTarget_ShowsTheImportLink() =>
        Assert.True(await CanImport(hasChild: false, azureResourceId: null));

    /// <summary>Adopting a hand-built subtree stays refused - that is what would re-stamp
    /// AzureResourceId on rows nobody imported, and the authority refuses it too.</summary>
    [Fact]
    public async Task APopulatedTargetWithNoAzureLink_HidesTheImportLink() =>
        Assert.False(await CanImport(hasChild: true, azureResourceId: null));

    /// <summary>Both remaining arms of the authority's gate, unchanged.</summary>
    [Fact]
    public async Task AFullyAllocatedTarget_HidesTheImportLink() =>
        Assert.False(await CanImport(hasChild: false, azureResourceId: VNetId, isFullyAllocated: true));

    [Fact]
    public async Task ATargetWithHostIpAssignments_HidesTheImportLink() =>
        Assert.False(await CanImport(hasChild: false, azureResourceId: VNetId, hasHostIp: true));
}
