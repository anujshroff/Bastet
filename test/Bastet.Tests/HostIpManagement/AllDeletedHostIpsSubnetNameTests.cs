using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.HostIpManagement;

public class AllDeletedHostIpsSubnetNameTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private const string AllDeletedView = "src/Bastet/Views/HostIp/AllDeletedHostIps.cshtml";

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Bastet.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Bastet.sln not found above test base directory");
    }

    private static string ReadView(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static async Task<List<AllDeletedHostIpItemViewModel>> RowsAsync(
        Action<BastetDbContext> seed)
    {
        using BastetDbContext context = TestDbContextFactory.CreateDbContext();
        seed(context);
        await context.SaveChangesAsync();

        IIpUtilityService ipUtility = new IpUtilityService();
        HostIpController controller = new(
            context,
            new HostIpValidationService(ipUtility, context),
            ipUtility,
            ControllerTestHelper.CreateMockUserContextService(),
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<HostIpController>.Instance);
        ControllerTestHelper.SetupController(controller);

        IActionResult result = await controller.AllDeletedHostIps();

        ViewResult view = Assert.IsType<ViewResult>(result);
        AllDeletedHostIpsViewModel model = Assert.IsType<AllDeletedHostIpsViewModel>(view.Model);
        return model.DeletedHostIps;
    }

    private static DeletedHostIpAssignment ArchivedHostIp(string ip, int originalSubnetId) => new()
    {
        OriginalIP = ip,
        Name = "box",
        OriginalSubnetId = originalSubnetId,
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        DeletedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
        DeletedBy = "tester"
    };

    private static Subnet LiveSubnet(int id, string name) => new()
    {
        Id = id,
        Name = name,
        NetworkAddress = $"10.{id}.0.0",
        Cidr = 24,
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task ALiveSubnetNamedUnknown_IsReportedByItsName_NotAsUnresolved()
    {
        List<AllDeletedHostIpItemViewModel> rows = await RowsAsync(context =>
        {
            context.Subnets.Add(LiveSubnet(1, "Unknown"));
            context.DeletedHostIpAssignments.Add(ArchivedHostIp("10.1.0.10", 1));
        });

        AllDeletedHostIpItemViewModel row = Assert.Single(rows);
        Assert.Equal("Unknown", row.SubnetName);
        Assert.DoesNotContain("Original Subnet ID", row.SubnetName);
    }

    [Fact]
    public async Task AnUnresolvableSubnet_CarriesTheOriginalIdInTheNameItself()
    {
        List<AllDeletedHostIpItemViewModel> rows = await RowsAsync(context =>
            context.DeletedHostIpAssignments.Add(ArchivedHostIp("10.9.0.10", 9)));

        AllDeletedHostIpItemViewModel row = Assert.Single(rows);
        Assert.Equal("Unknown (Original Subnet ID: 9)", row.SubnetName);
    }

    [Fact]
    public async Task ALiveSubnetNamedUnknown_AndAGenuinelyGoneSubnet_AreDistinguishable()
    {
        List<AllDeletedHostIpItemViewModel> rows = await RowsAsync(context =>
        {
            context.Subnets.Add(LiveSubnet(1, "Unknown"));
            context.DeletedHostIpAssignments.Add(ArchivedHostIp("10.1.0.10", 1));
            context.DeletedHostIpAssignments.Add(ArchivedHostIp("10.7.0.10", 7));
        });

        List<string> names = [.. rows.Select(r => r.SubnetName)];
        Assert.Contains("Unknown", names);
        Assert.Contains("Unknown (Original Subnet ID: 7)", names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public async Task AnArchivedSubnet_KeepsTheDeletedSuffix()
    {
        List<AllDeletedHostIpItemViewModel> rows = await RowsAsync(context =>
        {
            context.DeletedSubnets.Add(new DeletedSubnet
            {
                OriginalId = 4,
                Name = "Gone",
                NetworkAddress = "10.4.0.0",
                Cidr = 24,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                DeletedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            });
            context.DeletedHostIpAssignments.Add(ArchivedHostIp("10.4.0.10", 4));
        });

        Assert.Equal("Gone (deleted)", Assert.Single(rows).SubnetName);
    }

    [Fact]
    public void TheView_RendersTheSubnetNameWhole_AndNeverReDerivesResolutionFromIt()
    {
        string view = ReadView(AllDeletedView);

        Assert.Contains("<td>@hostIp.SubnetName</td>", view);
        Assert.DoesNotContain("\"Unknown\"", view);
        Assert.DoesNotContain("OriginalSubnetId", view);
    }
}
