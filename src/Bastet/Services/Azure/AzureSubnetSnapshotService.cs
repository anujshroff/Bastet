using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Services.Azure
{
    /// <summary>
    /// Default <see cref="IAzureSubnetSnapshotService"/>. Loads the tree once and derives the flags
    /// and counts in memory rather than issuing a query per subnet.
    /// </summary>
    public class AzureSubnetSnapshotService(BastetDbContext context) : IAzureSubnetSnapshotService
    {
        /// <inheritdoc/>
        public async Task<IReadOnlyList<ExistingSubnetSnapshot>> GetExistingSubnetsAsync()
        {
            List<Subnet> all = await context.Subnets.AsNoTracking().ToListAsync();
            HashSet<int> parentsWithChildren = GetParentsWithChildren(all);
            HashSet<int> subnetsWithHostIps = await GetSubnetsWithHostIpsAsync();

            return [.. all.Select(s => new ExistingSubnetSnapshot
            {
                Id = s.Id,
                Name = s.Name,
                NetworkAddress = s.NetworkAddress,
                Cidr = s.Cidr,
                HasChildSubnets = parentsWithChildren.Contains(s.Id),
                HasHostIpAssignments = subnetsWithHostIps.Contains(s.Id),
                IsFullyAllocated = s.IsFullyAllocated,
                AzureResourceId = s.AzureResourceId
            })];
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<AzureLinkedSubnetSnapshot>> GetAzureLinkedSubnetsAsync()
        {
            List<Subnet> all = await context.Subnets.AsNoTracking().ToListAsync();

            Dictionary<int, int> hostIpCounts = await context.HostIpAssignments
                .AsNoTracking()
                .GroupBy(h => h.SubnetId)
                .Select(g => new { SubnetId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SubnetId, x => x.Count);

            Dictionary<int, List<Subnet>> childrenByParent = all
                .Where(s => s.ParentSubnetId.HasValue)
                .GroupBy(s => s.ParentSubnetId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            List<AzureLinkedSubnetSnapshot> result = [];

            foreach (Subnet subnet in all.Where(s => !string.IsNullOrEmpty(s.AzureResourceId)))
            {
                List<Subnet> descendants = GetDescendants(subnet.Id, childrenByParent);

                int hostIps = hostIpCounts.GetValueOrDefault(subnet.Id);
                foreach (Subnet descendant in descendants)
                {
                    hostIps += hostIpCounts.GetValueOrDefault(descendant.Id);
                }

                result.Add(new AzureLinkedSubnetSnapshot
                {
                    Id = subnet.Id,
                    Name = subnet.Name,
                    NetworkAddress = subnet.NetworkAddress,
                    Cidr = subnet.Cidr,
                    AzureResourceId = subnet.AzureResourceId!,
                    IsFullyAllocated = subnet.IsFullyAllocated,
                    DescendantCount = descendants.Count,
                    HostIpCount = hostIps,
                    DescendantSubnetIds = [.. descendants.Select(d => d.Id)]
                });
            }

            return result;
        }

        private static HashSet<int> GetParentsWithChildren(List<Subnet> all) =>
            [.. all.Where(s => s.ParentSubnetId.HasValue).Select(s => s.ParentSubnetId!.Value).Distinct()];

        private async Task<HashSet<int>> GetSubnetsWithHostIpsAsync() =>
            await context.HostIpAssignments
                .AsNoTracking()
                .Select(h => h.SubnetId)
                .Distinct()
                .ToHashSetAsync();

        /// <summary>
        /// Every subnet beneath <paramref name="subnetId"/>. Tracks visited ids so a cycle in the
        /// data cannot spin forever.
        /// </summary>
        private static List<Subnet> GetDescendants(int subnetId, Dictionary<int, List<Subnet>> childrenByParent)
        {
            List<Subnet> descendants = [];
            HashSet<int> visited = [subnetId];
            Queue<int> queue = new();
            queue.Enqueue(subnetId);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (!childrenByParent.TryGetValue(current, out List<Subnet>? children))
                {
                    continue;
                }

                foreach (Subnet child in children)
                {
                    if (!visited.Add(child.Id))
                    {
                        continue;
                    }

                    descendants.Add(child);
                    queue.Enqueue(child.Id);
                }
            }

            return descendants;
        }
    }
}
