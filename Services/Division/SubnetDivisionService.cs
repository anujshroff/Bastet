using Bastet.Data;
using Bastet.Models;
using Bastet.Models.DTOs;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Bastet.Services.Division;

/// <summary>
/// Service implementation for subnet division operations
/// </summary>
public class SubnetDivisionService(
    BastetDbContext context,
    IIpUtilityService ipUtilityService) : ISubnetDivisionService
{

    /// <inheritdoc />
    public async Task<List<Subnet>> DivideSubnetAsync(
        Subnet parentSubnet,
        SubnetDivisionDto divisionDto,
        IEnumerable<Subnet>? existingChildren = null)
    {
        // Validate inputs
        ArgumentNullException.ThrowIfNull(parentSubnet);

        ArgumentNullException.ThrowIfNull(divisionDto);

        // Ensure target CIDR is valid for division
        if (!CanSubnetBeDivided(parentSubnet, divisionDto.TargetCidr))
        {
            throw new ArgumentException(
                $"Cannot divide subnet {parentSubnet.NetworkAddress}/{parentSubnet.Cidr} " +
                $"into subnets with CIDR {divisionDto.TargetCidr}. " +
                $"Target CIDR must be larger than parent's CIDR: {parentSubnet.Cidr}.");
        }

        // Get existing children if not provided
        existingChildren ??= await context.Subnets
                .Where(s => s.ParentSubnetId == parentSubnet.Id)
                .ToListAsync();

        // Get all possible divisions
        List<SubnetCalculation> possibleDivisions = GetPossibleDivisions(parentSubnet, divisionDto.TargetCidr);

        // Determine which divisions to create
        List<SubnetCalculation> divisionsToCreate;

        if (divisionDto.SpecificNetworks != null && divisionDto.SpecificNetworks.Count != 0)
        {
            // Create specific networks requested by the user
            divisionsToCreate = FilterRequestedNetworks(possibleDivisions, divisionDto.SpecificNetworks);
        }
        else if (divisionDto.Count.HasValue)
        {
            // Create a limited number of subnets
            divisionsToCreate = [.. possibleDivisions.Take(divisionDto.Count.Value)];
        }
        else
        {
            // Create all possible subnets
            divisionsToCreate = possibleDivisions;
        }

        // Check for overlaps with existing children
        if (existingChildren.Any())
        {
            divisionsToCreate = RemoveOverlappingNetworks(divisionsToCreate, existingChildren);
        }

        // Start creating subnets
        List<Subnet> createdSubnets = [];
        if (divisionsToCreate.Count == 0)
        {
            return createdSubnets; // No subnets to create
        }

        // No status field anymore

        // Create subnet entities
        for (int i = 0 ; i < divisionsToCreate.Count ; i++)
        {
            SubnetCalculation division = divisionsToCreate[i];

            Subnet subnet = new()
            {
                Name = GenerateSubnetName(
                    parentSubnet,
                    division.NetworkAddress,
                    division.Cidr,
                    divisionDto.NamePrefix,
                    i),
                NetworkAddress = division.NetworkAddress,
                Cidr = division.Cidr,
                Description = GenerateSubnetDescription(
                    parentSubnet,
                    division.NetworkAddress,
                    division.Cidr,
                    divisionDto.DescriptionTemplate,
                    i),
                Tags = divisionDto.Tags,
                ParentSubnetId = parentSubnet.Id,
                CreatedAt = DateTime.UtcNow
            };

            createdSubnets.Add(subnet);
        }

        // Save all subnets in a transaction
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

        try
        {
            context.Subnets.AddRange(createdSubnets);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return createdSubnets;
    }

    /// <inheritdoc />
    public List<SubnetCalculation> GetPossibleDivisions(Subnet parentSubnet, int targetCidr)
    {
        ArgumentNullException.ThrowIfNull(parentSubnet);

        if (!CanSubnetBeDivided(parentSubnet, targetCidr))
        {
            return [];
        }

        // Leverage the existing IP utility service to calculate possible subnets
        return [.. ipUtilityService.CalculatePossibleSubnets(
                parentSubnet.NetworkAddress,
                parentSubnet.Cidr,
                targetCidr)];
    }

    /// <inheritdoc />
    public bool CanSubnetBeDivided(Subnet subnet, int targetCidr)
    {
        ArgumentNullException.ThrowIfNull(subnet);

        // Can only divide if target CIDR is larger than source CIDR (smaller subnets)
        if (targetCidr <= subnet.Cidr)
        {
            return false;
        }

        // Ensure target CIDR is valid for the address family
        try
        {
            IPAddress ip = IPAddress.Parse(subnet.NetworkAddress);

            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && targetCidr > 32)
            {
                // IPv4 CIDR cannot be larger than 32
                return false;
            }

            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && targetCidr > 128)
            {
                // IPv6 CIDR cannot be larger than 128
                return false;
            }

            // Validate the network is properly aligned
            return ipUtilityService.IsValidSubnet(subnet.NetworkAddress, subnet.Cidr);
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public string GenerateSubnetName(
        Subnet parentSubnet,
        string childNetwork,
        int childCidr,
        string? namePrefix = null,
        int index = 0)
    {
        ArgumentNullException.ThrowIfNull(parentSubnet);

        if (string.IsNullOrEmpty(childNetwork))
        {
            throw new ArgumentNullException(nameof(childNetwork));
        }

        // Use prefix if provided
        if (!string.IsNullOrEmpty(namePrefix))
        {
            return $"{namePrefix.Trim()} {childNetwork}/{childCidr}";
        }

        // Otherwise, derive from parent name
        string baseName = !string.IsNullOrEmpty(parentSubnet.Name)
            ? parentSubnet.Name
            : $"Network-{parentSubnet.NetworkAddress}/{parentSubnet.Cidr}";

        return $"{baseName}-Subnet-{index + 1}";
    }

    /// <inheritdoc />
    public string? GenerateSubnetDescription(
        Subnet parentSubnet,
        string childNetwork,
        int childCidr,
        string? descriptionTemplate = null,
        int index = 0)
    {
        ArgumentNullException.ThrowIfNull(parentSubnet);

        if (string.IsNullOrEmpty(childNetwork))
        {
            throw new ArgumentNullException(nameof(childNetwork));
        }

        // If template provided, replace placeholders
        if (!string.IsNullOrEmpty(descriptionTemplate))
        {
            return descriptionTemplate
                .Replace("{ParentName}", parentSubnet.Name)
                .Replace("{ParentNetwork}", parentSubnet.NetworkAddress)
                .Replace("{ParentCidr}", parentSubnet.Cidr.ToString())
                .Replace("{ChildNetwork}", childNetwork)
                .Replace("{ChildCidr}", childCidr.ToString())
                .Replace("{Index}", (index + 1).ToString());
        }

        // Generate a basic description
        return $"Subnet {childNetwork}/{childCidr} created by dividing {parentSubnet.NetworkAddress}/{parentSubnet.Cidr}";
    }

    #region Helper Methods

    /// <summary>
    /// Filter possible divisions based on requested networks
    /// </summary>
    private static List<SubnetCalculation> FilterRequestedNetworks(List<SubnetCalculation> possibleDivisions, List<string> requestedNetworks)
    {
        List<SubnetCalculation> result = [];

        // Normalize and validate requested networks
        foreach (string network in requestedNetworks)
        {
            // Find the matching network in possible divisions
            SubnetCalculation? matchingDivision = possibleDivisions.FirstOrDefault(d =>
                string.Equals(d.NetworkAddress, network, StringComparison.OrdinalIgnoreCase));

            if (matchingDivision != null)
            {
                result.Add(matchingDivision);
            }
        }

        return result;
    }

    /// <summary>
    /// Remove networks that overlap with existing children
    /// </summary>
    private List<SubnetCalculation> RemoveOverlappingNetworks(
        List<SubnetCalculation> divisions,
        IEnumerable<Subnet> existingChildren)
    {
        List<SubnetCalculation> result = [];

        foreach (SubnetCalculation division in divisions)
        {
            bool overlaps = false;

            foreach (Subnet child in existingChildren)
            {
                // Check if division contains child or child contains division
                bool divisionContainsChild = ipUtilityService.IsSubnetContainedInParent(
                    child.NetworkAddress, child.Cidr,
                    division.NetworkAddress, division.Cidr);

                bool childContainsDivision = ipUtilityService.IsSubnetContainedInParent(
                    division.NetworkAddress, division.Cidr,
                    child.NetworkAddress, child.Cidr);

                if (divisionContainsChild || childContainsDivision)
                {
                    overlaps = true;
                    break;
                }
            }

            if (!overlaps)
            {
                result.Add(division);
            }
        }

        return result;
    }

    #endregion
}
