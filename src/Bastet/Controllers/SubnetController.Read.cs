using Bastet.Models;
using Bastet.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{
    [Authorize(Policy = "RequireViewRole")]
    public async Task<IActionResult> Index()
    {
        // Get all subnets with their relationships
        List<Subnet> allSubnets = await context.Subnets
            .Include(s => s.ChildSubnets)
            .ToListAsync();

        // Build the subnet hierarchy
        List<Subnet> rootSubnets = [.. allSubnets.Where(s => !s.ParentSubnetId.HasValue)];
        List<SubnetTreeViewModel> hierarchicalSubnets = [];

        foreach (Subnet? rootSubnet in rootSubnets)
        {
            hierarchicalSubnets.Add(BuildSubnetTreeViewModel(rootSubnet, allSubnets));
        }

        return View(hierarchicalSubnets);
    }

    [Authorize(Policy = "RequireViewRole")]
    public async Task<IActionResult> Details(int id)
    {
        Subnet? subnet = await context.Subnets
            .Include(s => s.ChildSubnets)
            .Include(s => s.HostIpAssignments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (subnet == null)
        {
            // Use our custom 404 page with helpful context
            return RedirectToAction("HttpStatusCodeHandler", "Error", new
            {
                statusCode = 404,
                errorMessage = $"Subnet with ID {id} could not be found."
            });
        }

        SubnetDetailsViewModel viewModel = new()
        {
            Id = subnet.Id,
            Name = subnet.Name,
            NetworkAddress = subnet.NetworkAddress,
            Cidr = subnet.Cidr,
            Description = subnet.Description,
            Tags = subnet.Tags,
            ParentSubnetId = subnet.ParentSubnetId,
            CreatedAt = subnet.CreatedAt,
            LastModifiedAt = subnet.LastModifiedAt,
            CreatedBy = subnet.CreatedBy,
            ModifiedBy = subnet.ModifiedBy,
            IsFullyAllocated = subnet.IsFullyAllocated,
            // Calculate subnet properties
            SubnetMask = ipUtilityService.CalculateSubnetMask(subnet.Cidr),
            BroadcastAddress = ipUtilityService.CalculateBroadcastAddress(subnet.NetworkAddress, subnet.Cidr),
            TotalIpAddresses = ipUtilityService.CalculateTotalIpAddresses(subnet.Cidr),
            UsableIpAddresses = ipUtilityService.CalculateUsableIpAddresses(subnet.Cidr),
            // Include children, ordered by network address
            ChildSubnets = [.. subnet.ChildSubnets
                .OrderBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[0])
                .ThenBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[1])
                .ThenBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[2])
                .ThenBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[3])
                .Select(c => new SubnetViewModel
                {
                    Id = c.Id,
                    Name = c.Name,
                    NetworkAddress = c.NetworkAddress,
                    Cidr = c.Cidr
                })],
            // Include host IP assignments if any
            HostIpAssignments = [.. subnet.HostIpAssignments
                .OrderBy(h => IPAddress.Parse(h.IP).GetAddressBytes()[0])
                .ThenBy(h => IPAddress.Parse(h.IP).GetAddressBytes()[1])
                .ThenBy(h => IPAddress.Parse(h.IP).GetAddressBytes()[2])
                .ThenBy(h => IPAddress.Parse(h.IP).GetAddressBytes()[3])
                .Select(h => new HostIpViewModel
                {
                    IP = h.IP,
                    Name = h.Name,
                    CreatedAt = h.CreatedAt,
                    CreatedBy = h.CreatedBy,
                    LastModifiedAt = h.LastModifiedAt,
                    ModifiedBy = h.ModifiedBy
                })],
            // Get unallocated IP ranges, factoring in both child subnets and host IPs
            UnallocatedRanges = [.. ipUtilityService.CalculateUnallocatedRanges(
                subnet.NetworkAddress,
                subnet.Cidr,
                subnet.ChildSubnets,
                subnet.HostIpAssignments)]
        };

        // Try to get parent subnet if exists
        if (subnet.ParentSubnetId.HasValue)
        {
            Subnet? parentSubnet = await context.Subnets.FindAsync(subnet.ParentSubnetId.Value);
            if (parentSubnet != null)
            {
                viewModel.ParentSubnetName = parentSubnet.Name;
                viewModel.ParentNetworkAddress = $"{parentSubnet.NetworkAddress}/{parentSubnet.Cidr}";
            }
        }

        return View(viewModel);
    }
}
