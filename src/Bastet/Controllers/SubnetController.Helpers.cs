using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Sockets;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{

    private async Task<int> CountAllDescendants(int subnetId)
    {

        List<Subnet> allSubnets = await context.Subnets.ToListAsync();

        int descendantCount = 0;

        HashSet<int> processedIds = [];

        Queue<int> queue = new();
        queue.Enqueue(subnetId);
        processedIds.Add(subnetId);

        while (queue.Count > 0)
        {
            int currentId = queue.Dequeue();

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

    private async Task<List<Subnet>> GetAllDescendantsOrdered(int subnetId, List<Subnet>? treeCache = null)
    {

        List<Subnet> allSubnets = treeCache ?? await context.Subnets.ToListAsync();

        Dictionary<int, Subnet> subnetDict = allSubnets.ToDictionary(s => s.Id);

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

        List<Subnet> result = [];
        CollectDescendants(subnetId, tree, subnetDict, result);

        result.RemoveAll(s => s.Id == subnetId);

        return result;
    }

    private static void CollectDescendants(int subnetId, Dictionary<int, List<int>> tree,
                                  Dictionary<int, Subnet> subnetDict, List<Subnet> result)
    {

        if (tree.TryGetValue(subnetId, out List<int>? value))
        {
            foreach (int childId in value)
            {
                CollectDescendants(childId, tree, subnetDict, result);
            }
        }

        if (subnetDict.TryGetValue(subnetId, out Subnet? value2))
        {
            result.Add(value2);
        }
    }

    private bool SubnetExists(int id) => context.Subnets.Any(e => e.Id == id);

    private Task<List<Subnet>> LoadSubnetTreeForBatchAsync() =>
        context.Subnets.AsNoTracking().ToListAsync();

    private async Task<bool> ValidateSubnetCreation(CreateSubnetViewModel viewModel, List<Subnet>? treeCache = null)
    {

        if (!IPAddress.TryParse(viewModel.NetworkAddress, out IPAddress? parsedNetwork)
            || parsedNetwork.AddressFamily != AddressFamily.InterNetwork
            || parsedNetwork.ToString() != viewModel.NetworkAddress)
        {
            ModelState.AddModelError("NetworkAddress",
                $"'{viewModel.NetworkAddress}' is not a valid IPv4 network address. " +
                $"Use dotted-quad notation with no leading zeroes (e.g. 10.0.0.0).");
            return false;
        }

        Subnet? parentSubnet = null;
        if (viewModel.ParentSubnetId.HasValue)
        {
            parentSubnet = await context.Subnets
                .Include(s => s.HostIpAssignments)
                .FirstOrDefaultAsync(s => s.Id == viewModel.ParentSubnetId.Value);

            if (parentSubnet == null)
            {
                ModelState.AddModelError("ParentSubnetId", "Selected parent subnet does not exist");
                return false;
            }

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

            if (parentSubnet.IsFullyAllocated)
            {
                ModelState.AddModelError("ParentSubnetId",
                    "Cannot create a child subnet under a subnet marked as fully allocated.");
                return false;
            }

            if (!ipUtilityService.IsSubnetContainedInParent(
                viewModel.NetworkAddress, viewModel.Cidr,
                parentSubnet.NetworkAddress, parentSubnet.Cidr))
            {
                ModelState.AddModelError("NetworkAddress",
                    $"Child subnet must be contained within the parent subnet range. " +
                    $"Parent subnet is {parentSubnet.NetworkAddress}/{parentSubnet.Cidr}");
                return false;
            }

            if (viewModel.Cidr <= parentSubnet.Cidr)
            {
                ModelState.AddModelError("Cidr",
                    "Child subnet CIDR must be larger than parent subnet CIDR. " +
                    $"Parent subnet CIDR is {parentSubnet.Cidr}");
                return false;
            }
        }

        if (!ipUtilityService.IsValidSubnet(viewModel.NetworkAddress, viewModel.Cidr))
        {
            ModelState.AddModelError("NetworkAddress",
                $"Network address {viewModel.NetworkAddress} is not valid for CIDR /{viewModel.Cidr}. " +
                $"The network address must align with the subnet boundary.");
            return false;
        }

        Subnet? existingSubnet = await context.Subnets
            .FirstOrDefaultAsync(s => s.NetworkAddress == viewModel.NetworkAddress &&
                                   s.Cidr == viewModel.Cidr);

        if (existingSubnet != null)
        {
            ModelState.AddModelError("NetworkAddress",
                $"A subnet with network {viewModel.NetworkAddress}/{viewModel.Cidr} already exists");
            return false;
        }

        List<Subnet> allSubnets = treeCache ?? await context.Subnets.ToListAsync();

        Subnet? bestParent = null;
        int bestParentCidr = -1;

        foreach (Subnet? candidateParent in allSubnets)
        {

            if (candidateParent.NetworkAddress == viewModel.NetworkAddress && candidateParent.Cidr == viewModel.Cidr)
            {
                continue;
            }

            if (ipUtilityService.IsSubnetContainedInParent(
                viewModel.NetworkAddress, viewModel.Cidr,
                candidateParent.NetworkAddress, candidateParent.Cidr))
            {

                if (candidateParent.Cidr > bestParentCidr)
                {
                    bestParent = candidateParent;
                    bestParentCidr = candidateParent.Cidr;
                }
            }
        }

        if (bestParent != null)
        {
            if (!viewModel.ParentSubnetId.HasValue)
            {

                ModelState.AddModelError("ParentSubnetId",
                    $"This subnet must be a child of subnet {bestParent.Name} " +
                    $"({bestParent.NetworkAddress}/{bestParent.Cidr}).");
                return false;
            }
            else if (viewModel.ParentSubnetId.Value != bestParent.Id)
            {

                Subnet? selectedParent = await context.Subnets.FindAsync(viewModel.ParentSubnetId.Value);

                if (selectedParent != null && bestParent.Cidr > selectedParent.Cidr)
                {

                    ModelState.AddModelError("ParentSubnetId",
                        $"A more specific parent subnet exists: {bestParent.Name} " +
                        $"({bestParent.NetworkAddress}/{bestParent.Cidr}). Please select it instead.");
                    return false;
                }
            }
        }

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
            UsableIpAddresses = ipUtilityService.CalculateUsableIpAddresses(subnet.Cidr),
            ParentSubnetId = subnet.ParentSubnetId,
            ChildSubnets = []
        };

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
