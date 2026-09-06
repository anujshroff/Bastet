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

        List<Subnet> allSubnets = await context.Subnets
            .Include(s => s.ChildSubnets)
            .ToListAsync();

        List<Subnet> rootSubnets = [.. allSubnets.Where(s => !s.ParentSubnetId.HasValue)];
        List<SubnetTreeViewModel> hierarchicalSubnets = [];

        foreach (Subnet? rootSubnet in rootSubnets)
        {
            hierarchicalSubnets.Add(BuildSubnetTreeViewModel(rootSubnet));
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

            return this.RedirectToErrorPage(404, $"Subnet with ID {id} could not be found.");
        }

        SubnetDetailsViewModel viewModel = new()
        {
            Id = subnet.Id,
            Name = subnet.Name,
            NetworkAddress = subnet.NetworkAddress,
            Cidr = subnet.Cidr,
            Description = subnet.Description,
            Tags = subnet.Tags,
            AzureResourceId = subnet.AzureResourceId,
            ParentSubnetId = subnet.ParentSubnetId,
            CreatedAt = subnet.CreatedAt,
            LastModifiedAt = subnet.LastModifiedAt,
            CreatedBy = subnet.CreatedBy,
            ModifiedBy = subnet.ModifiedBy,
            IsFullyAllocated = subnet.IsFullyAllocated,

            SubnetMask = ipUtilityService.CalculateSubnetMask(subnet.Cidr),

            BroadcastAddress = subnet.Cidr < 31
                ? ipUtilityService.CalculateBroadcastAddress(subnet.NetworkAddress, subnet.Cidr)
                : string.Empty,
            TotalIpAddresses = ipUtilityService.CalculateTotalIpAddresses(subnet.Cidr),
            UsableIpAddresses = ipUtilityService.CalculateUsableIpAddresses(subnet.Cidr),

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
                    LastModifiedAt = h.LastModifiedAt
                })],

            UnallocatedRanges = [.. ipUtilityService.CalculateUnallocatedRanges(
                subnet.NetworkAddress,
                subnet.Cidr,
                subnet.ChildSubnets,
                subnet.HostIpAssignments)]
        };

        viewModel.ChildSubnetSuggestions = viewModel.CanAddChildSubnet
            ? [.. ipUtilityService.SuggestChildSubnets(subnet.Cidr, viewModel.UnallocatedRanges)]
            : [];

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
