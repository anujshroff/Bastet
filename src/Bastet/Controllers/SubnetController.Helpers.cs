using Bastet.Models;
using Bastet.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{
    // Helper method to count all descendants of a subnet using in-memory approach
    private async Task<int> CountAllDescendants(int subnetId)
    {
        // Get all subnets from the database
        List<Subnet> allSubnets = await context.Subnets.ToListAsync();

        // Store the count of descendants
        int descendantCount = 0;

        // Set to keep track of processed IDs to avoid circular references
        HashSet<int> processedIds = [];

        // Queue for breadth-first traversal
        Queue<int> queue = new();
        queue.Enqueue(subnetId);
        processedIds.Add(subnetId);

        while (queue.Count > 0)
        {
            int currentId = queue.Dequeue();

            // Find all direct children of the current subnet
            List<Subnet> childSubnets = [.. allSubnets.Where(s => s.ParentSubnetId == currentId)];

            foreach (Subnet? child in childSubnets)
            {
                if (!processedIds.Contains(child.Id))
                {
                    descendantCount++;
                    queue.Enqueue(child.Id);
                    processedIds.Add(child.Id);
                }
            }
        }

        return descendantCount;
    }

    // Helper method to get all descendants ordered for deletion (deepest children first)
    private async Task<List<Subnet>> GetAllDescendantsOrdered(int subnetId)
    {
        // Start with all subnets
        List<Subnet> allSubnets = await context.Subnets.ToListAsync();

        // Create a dictionary for faster lookup
        Dictionary<int, Subnet> subnetDict = allSubnets.ToDictionary(s => s.Id);

        // Build a tree structure
        Dictionary<int, List<int>> tree = [];
        foreach (Subnet? s in allSubnets)
        {
            if (!tree.ContainsKey(s.Id))
            {
                tree[s.Id] = [];
            }

            if (s.ParentSubnetId.HasValue)
            {
                if (!tree.TryGetValue(s.ParentSubnetId.Value, out List<int>? value))
                {
                    value = [];
                    tree[s.ParentSubnetId.Value] = value;
                }

                value.Add(s.Id);
            }
        }

        // Recursively collect descendants in order
        List<Subnet> result = [];
        CollectDescendants(subnetId, tree, subnetDict, result);

        // Remove the root subnet itself (it will be added later)
        result.RemoveAll(s => s.Id == subnetId);

        return result;
    }

    // Helper method for recursively collecting descendants
    private static void CollectDescendants(int subnetId, Dictionary<int, List<int>> tree,
                                  Dictionary<int, Subnet> subnetDict, List<Subnet> result)
    {
        // Process children first (depth-first)
        if (tree.TryGetValue(subnetId, out List<int>? value))
        {
            foreach (int childId in value)
            {
                CollectDescendants(childId, tree, subnetDict, result);
            }
        }

        // Add this subnet to the result
        if (subnetDict.TryGetValue(subnetId, out Subnet? value2))
        {
            result.Add(value2);
        }
    }

    private bool SubnetExists(int id) => context.Subnets.Any(e => e.Id == id);

    private SubnetTreeViewModel BuildSubnetTreeViewModel(Subnet subnet, List<Subnet> allSubnets)
    {
        SubnetTreeViewModel viewModel = new()
        {
            Id = subnet.Id,
            Name = subnet.Name,
            NetworkAddress = subnet.NetworkAddress,
            Cidr = subnet.Cidr,
            Description = subnet.Description,
            SubnetMask = ipUtilityService.CalculateSubnetMask(subnet.Cidr),
            TotalIpAddresses = ipUtilityService.CalculateTotalIpAddresses(subnet.Cidr),
            UsableIpAddresses = ipUtilityService.CalculateUsableIpAddresses(subnet.Cidr),
            ParentSubnetId = subnet.ParentSubnetId,
            ChildSubnets = []
        };

        // Recursively build child subnet trees, ordered by network address
        foreach (Subnet? childSubnet in subnet.ChildSubnets
            .OrderBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[0])
            .ThenBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[1])
            .ThenBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[2])
            .ThenBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[3]))
        {
            viewModel.ChildSubnets.Add(BuildSubnetTreeViewModel(childSubnet, allSubnets));
        }

        return viewModel;
    }
}
