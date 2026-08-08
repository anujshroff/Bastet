using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Data.Common;

namespace Bastet.Tests.Azure;

internal sealed class WholeTableSubnetReadCounter : DbCommandInterceptor
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    private void Tally(DbCommand command)
    {
        string text = command.CommandText;
        bool readsSubnets = text.Contains("FROM \"Subnets\"") || text.Contains("FROM [Subnets]");
        if (readsSubnets && !text.Contains("WHERE"))
        {
            Interlocked.Increment(ref _count);
        }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Tally(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Tally(command);
        return ValueTask.FromResult(result);
    }
}

[Collection(AzureFeatureFlagCollection.Name)]
public class SubnetControllerAzureReconcileScalingTests : IDisposable
{
    private const string SubId = "11111111-1111-1111-1111-111111111111";

    public SubnetControllerAzureReconcileScalingTests() =>
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "true");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", null);
        GC.SuppressFinalize(this);
    }

    private sealed record Outcome(int WholeTableReads, int SubnetsArchived, int HostIpsArchived, int SubnetsRemaining);

    private static async Task<Outcome> ArchiveStaleTargetsAsync(int targetCount)
    {
        WholeTableSubnetReadCounter counter = new();

        DbContextOptions<BastetDbContext> options = new DbContextOptionsBuilder<BastetDbContext>()
            .UseSqlite("DataSource=:memory:")
            .AddInterceptors(counter)
            .Options;

        using BastetDbContext context = new(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();

        IIpUtilityService ipUtilityService = new IpUtilityService();
        SubnetController controller = new(
            context,
            ipUtilityService,
            new SubnetValidationService(ipUtilityService),
            new HostIpValidationService(ipUtilityService, context),
            ControllerTestHelper.CreateMockUserContextService(),
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<SubnetController>.Instance);
        ControllerTestHelper.SetupController(controller);
        controller.Url = Mock.Of<IUrlHelper>();

        int nextId = 1;
        List<int> targets = [];

        for (int v = 0; v < targetCount; v++)
        {
            int rootId = nextId++;
            targets.Add(rootId);
            context.Subnets.Add(new Subnet
            {
                Id = rootId,
                Name = $"vnet-gone-{v}",
                NetworkAddress = $"10.{v}.0.0",
                Cidr = 16,
                AzureResourceId = $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-gone-{v}",
                CreatedAt = DateTime.UtcNow
            });

            int childId = nextId++;
            context.Subnets.Add(new Subnet
            {
                Id = childId,
                Name = $"child-{v}",
                NetworkAddress = $"10.{v}.1.0",
                Cidr = 24,
                ParentSubnetId = rootId,
                CreatedAt = DateTime.UtcNow
            });

            int grandchildId = nextId++;
            context.Subnets.Add(new Subnet
            {
                Id = grandchildId,
                Name = $"grandchild-{v}",
                NetworkAddress = $"10.{v}.1.0",
                Cidr = 26,
                ParentSubnetId = childId,
                CreatedAt = DateTime.UtcNow
            });

            context.HostIpAssignments.Add(new HostIpAssignment
            {
                IP = $"10.{v}.1.5",
                Name = $"host-{v}",
                SubnetId = grandchildId,
                CreatedAt = DateTime.UtcNow
            });
        }

        for (int pad = 0; pad < 200; pad++)
        {
            context.Subnets.Add(new Subnet
            {
                Id = nextId++,
                Name = $"pad-{pad}",
                NetworkAddress = $"172.{pad / 256}.{pad % 256}.0",
                Cidr = 24,
                CreatedAt = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        MockAzureService azure = new();
        AzureSubnetSnapshotService snapshots = new(context);

        IActionResult result = await controller.BulkDeleteStaleAzureSubnets(
            new AzureReconcileDeleteDto
            {
                SubscriptionId = SubId,
                SubnetIds = targets,
                Confirmation = "approved",
                Statuses = await AzureReconcileApproval.ForAsync(azure, snapshots, SubId, targets)
            },
            azure,
            new AzureReconciler(new IpUtilityService()),
            snapshots);

        Assert.IsType<OkObjectResult>(result);

        Outcome outcome = new(
            counter.Count,
            await context.DeletedSubnets.CountAsync(TestContext.Current.CancellationToken),
            await context.DeletedHostIpAssignments.CountAsync(TestContext.Current.CancellationToken),
            await context.Subnets.CountAsync(TestContext.Current.CancellationToken));

        context.Database.CloseConnection();
        return outcome;
    }

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_WholeTableReads_DoNotGrowWithTargetCount()
    {
        Outcome two = await ArchiveStaleTargetsAsync(2);
        Outcome eight = await ArchiveStaleTargetsAsync(8);

        Assert.Equal(two.WholeTableReads, eight.WholeTableReads);

        Assert.Equal(6, two.SubnetsArchived);
        Assert.Equal(24, eight.SubnetsArchived);
    }

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_NestedSubtreesWithHostIps_AreFullyArchived()
    {
        Outcome outcome = await ArchiveStaleTargetsAsync(8);

        Assert.Equal(24, outcome.SubnetsArchived);
        Assert.Equal(8, outcome.HostIpsArchived);

        Assert.Equal(200, outcome.SubnetsRemaining);
    }
}
