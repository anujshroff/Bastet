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

/// <summary>
/// Counts unfiltered reads of the Subnets table - the ones with no WHERE clause, which materialise
/// the entire table however few rows the caller actually wants.
/// </summary>
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

/// <summary>
/// The reconcile bulk delete archives every selected subtree inside the global write lock, so its
/// cost while holding that lock is what decides whether an unrelated user's write is merely delayed
/// or actually refused.
/// </summary>
/// <remarks>
/// Regression for round-10 J1. The archive path read the whole Subnets table twice per selected
/// target - once in the loop and once inside <c>ArchiveSubnetSubtreeAsync</c> - so a request cost
/// O(targets x table). With 200 targets against 66,000 subnets it held
/// <c>Bastet:SubnetOperations</c> for ~57 s and a concurrent <c>POST /Subnet/Create</c> from a second
/// process was refused after 30.3 s with the app's high-concurrency message.
/// <para>
/// The pin is deliberately a comparison rather than a fixed number: what must stay true is that the
/// count does not grow with the number of targets. Asserting an exact count would fail on unrelated
/// query changes while still permitting the defect to come back.
/// </para>
/// <para>
/// The subtrees here are nested and carry host IPs on purpose. The cache threaded through the
/// archive path must be a tracking read, and a flat, leaf-only workload cannot detect that: the
/// per-subnet host-IP <c>Include</c> tracks a fresh instance of every descendant, so removing a
/// detached duplicate throws "another instance with the same key value is already being tracked" -
/// but only once a target actually has descendants.
/// </para>
/// </remarks>
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

    /// <summary>
    /// Archives <paramref name="targetCount"/> stale VNet-linked subtrees, each a root with a child,
    /// a grandchild and one host IP, alongside 200 unrelated subnets that are never selected.
    /// </summary>
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

        // Never selected and never archived - present so that an unfiltered read is materially more
        // expensive than a targeted one, which is the whole point of the defect.
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
            new AzureReconciler(),
            snapshots);

        // An empty-but-successful Azure inventory means every VNet really is gone, so all of them
        // are archived. Asserted here so a failure inside the loop is not read as a scaling result.
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

        // Before the fix this was 1 + 2 per target: 5 reads for two targets and 17 for eight.
        Assert.Equal(two.WholeTableReads, eight.WholeTableReads);

        // And the extra targets really were archived, so the counts above compare like with like.
        Assert.Equal(6, two.SubnetsArchived);
        Assert.Equal(24, eight.SubnetsArchived);
    }

    [Fact]
    public async Task BulkDeleteStaleAzureSubnets_NestedSubtreesWithHostIps_AreFullyArchived()
    {
        Outcome outcome = await ArchiveStaleTargetsAsync(8);

        // Root, child and grandchild for each of the eight targets.
        Assert.Equal(24, outcome.SubnetsArchived);
        Assert.Equal(8, outcome.HostIpsArchived);

        // The 200 unrelated subnets are untouched.
        Assert.Equal(200, outcome.SubnetsRemaining);
    }
}
