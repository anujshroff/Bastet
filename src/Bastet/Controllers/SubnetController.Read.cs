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
            // Calculate subnet properties
            SubnetMask = ipUtilityService.CalculateSubnetMask(subnet.Cidr),
            // Only below /31. A /31 has no broadcast address (RFC 3021 gives both addresses to the
            // link) and a /32 is a single host, and this application agrees: HostIpValidationService
            // applies the network/broadcast reservation only when Cidr < 31, so both /31 addresses
            // and the single /32 address are assignable - and were being assigned while this card
            // named one of them the broadcast, on the same page that listed it as a host IP.
            BroadcastAddress = subnet.Cidr < 31
                ? ipUtilityService.CalculateBroadcastAddress(subnet.NetworkAddress, subnet.Cidr)
                : string.Empty,
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

        // Check if this subnet can be imported from Azure
        bool azureImportEnabled = bool.TryParse(
            Environment.GetEnvironmentVariable("BASTET_AZURE_IMPORT"), out bool result) && result;

        // This predicate must stay set-equivalent to the one AzureController.Import enforces, which
        // is the authority. N4 relaxed that one to admit a top-up - a populated target that already
        // carries an Azure link - and did not touch this copy, so the two became mutually exclusive
        // by construction: whenever the server would accept a top-up, the only link in the whole
        // application that reaches /Azure/Import/{id} was not rendered. The button therefore
        // disappeared permanently after the first successful single-VNet import, which is the steady
        // state of the feature.
        bool isTopUp = subnet.ChildSubnets.Count != 0 && !string.IsNullOrEmpty(subnet.AzureResourceId);

        ViewBag.CanImportFromAzure =
            userContextService.UserHasRole(ApplicationRoles.Admin) &&
            azureImportEnabled &&
            (subnet.ChildSubnets.Count == 0 || isTopUp) &&
            subnet.HostIpAssignments.Count == 0 &&
            !subnet.IsFullyAllocated;

        return View(viewModel);
    }
}
