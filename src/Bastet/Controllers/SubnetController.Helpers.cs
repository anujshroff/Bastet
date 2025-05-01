using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services.Validation;
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

    // Helper method to validate subnet creation
    private async Task<bool> ValidateSubnetCreation(CreateSubnetViewModel viewModel)
    {
        // Check if parent subnet exists if specified
        Subnet? parentSubnet = null;
        if (viewModel.ParentSubnetId.HasValue)
        {
            parentSubnet = await context.Subnets
                .FirstOrDefaultAsync(s => s.Id == viewModel.ParentSubnetId.Value);

            if (parentSubnet == null)
            {
                ModelState.AddModelError("ParentSubnetId", "Selected parent subnet does not exist");
                return false;
            }

            // Validate that parent doesn't have host IPs
            ValidationResult hostIpValidation = subnetValidationService.ValidateParentCanHaveChildSubnets(
                parentSubnet.Id,
                parentSubnet.HostIpAssignments);

            if (!hostIpValidation.IsValid)
            {
                foreach (ValidationError error in hostIpValidation.Errors)
                {
                    ModelState.AddModelError("ParentSubnetId", error.Message);
                }

                return false;
            }

            // Validate that child subnet is within parent subnet range
            if (!ipUtilityService.IsSubnetContainedInParent(
                viewModel.NetworkAddress, viewModel.Cidr,
                parentSubnet.NetworkAddress, parentSubnet.Cidr))
            {
                ModelState.AddModelError("NetworkAddress",
                    $"Child subnet must be contained within the parent subnet range. " +
                    $"Parent subnet is {parentSubnet.NetworkAddress}/{parentSubnet.Cidr}");
                return false;
            }

            // Validate that child CIDR is larger than parent
            if (viewModel.Cidr <= parentSubnet.Cidr)
            {
                ModelState.AddModelError("Cidr",
                    "Child subnet CIDR must be larger than parent subnet CIDR. " +
                    $"Parent subnet CIDR is {parentSubnet.Cidr}");
                return false;
            }
        }

        // Explicitly validate network address and CIDR alignment
        if (!ipUtilityService.IsValidSubnet(viewModel.NetworkAddress, viewModel.Cidr))
        {
            ModelState.AddModelError("NetworkAddress",
                $"Network address {viewModel.NetworkAddress} is not valid for CIDR /{viewModel.Cidr}. " +
                $"The network address must align with the subnet boundary.");
            return false;
        }

        // Check for subnet with same network/cidr
        Subnet? existingSubnet = await context.Subnets
            .FirstOrDefaultAsync(s => s.NetworkAddress == viewModel.NetworkAddress &&
                                   s.Cidr == viewModel.Cidr);

        if (existingSubnet != null)
        {
            ModelState.AddModelError("NetworkAddress",
                $"A subnet with network {viewModel.NetworkAddress}/{viewModel.Cidr} already exists");
            return false;
        }

        // Always check for the most specific parent subnet
        // Get all existing subnets
        List<Subnet> allSubnets = await context.Subnets.ToListAsync();

        // Find closest containing subnet (most specific parent)
        Subnet? bestParent = null;
        int bestParentCidr = -1;

        foreach (Subnet? candidateParent in allSubnets)
        {
            // Skip the subnet if it's the same as our input
            if (candidateParent.NetworkAddress == viewModel.NetworkAddress && candidateParent.Cidr == viewModel.Cidr)
            {
                continue;
            }

            // Check if this subnet would contain our new subnet
            if (ipUtilityService.IsSubnetContainedInParent(
                viewModel.NetworkAddress, viewModel.Cidr,
                candidateParent.NetworkAddress, candidateParent.Cidr))
            {
                // If we found a better (more specific) parent
                if (candidateParent.Cidr > bestParentCidr)
                {
                    bestParent = candidateParent;
                    bestParentCidr = candidateParent.Cidr;
                }
            }
        }

        // If we found a better parent than what was selected
        if (bestParent != null)
        {
            if (!viewModel.ParentSubnetId.HasValue)
            {
                // No parent was selected, but one is required
                ModelState.AddModelError("ParentSubnetId",
                    $"This subnet must be a child of subnet {bestParent.Name} " +
                    $"({bestParent.NetworkAddress}/{bestParent.Cidr}).");
                return false;
            }
            else if (viewModel.ParentSubnetId.Value != bestParent.Id)
            {
                // Wrong parent was selected
                Subnet? selectedParent = await context.Subnets.FindAsync(viewModel.ParentSubnetId.Value);

                if (selectedParent != null && bestParent.Cidr > selectedParent.Cidr)
                {
                    // Selected parent is less specific than best parent
                    ModelState.AddModelError("ParentSubnetId",
                        $"A more specific parent subnet exists: {bestParent.Name} " +
                        $"({bestParent.NetworkAddress}/{bestParent.Cidr}). Please select it instead.");
                    return false;
                }
            }
        }

        // Check if new subnet would contain any existing subnets
        foreach (Subnet? potentialChildSubnet in allSubnets)
        {
            if (ipUtilityService.IsSubnetContainedInParent(
                potentialChildSubnet.NetworkAddress, potentialChildSubnet.Cidr,
                viewModel.NetworkAddress, viewModel.Cidr))
            {
                ModelState.AddModelError("NetworkAddress",
                    $"This subnet would contain existing subnet {potentialChildSubnet.Name} " +
                    $"({potentialChildSubnet.NetworkAddress}/{potentialChildSubnet.Cidr}). This would create an invalid hierarchy.");
                return false;
            }
        }

        // All validations passed
        return true;
    }

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
