using Bastet.Models;
using Bastet.Models.DTOs;

namespace Bastet.Services.Validation;

/// <summary>
/// Service for validating subnet operations
/// </summary>
public class SubnetValidationService(IIpUtilityService ipUtilityService) : ISubnetValidationService
{

    // Error codes
    private const string INVALID_NETWORK_FORMAT = "INVALID_NETWORK_FORMAT";
    private const string INVALID_CIDR_VALUE = "INVALID_CIDR_VALUE";
    private const string NETWORK_CIDR_MISMATCH = "NETWORK_CIDR_MISMATCH";
    private const string PARENT_NOT_FOUND = "PARENT_NOT_FOUND";
    private const string INVALID_CIDR_HIERARCHY = "INVALID_CIDR_HIERARCHY";
    private const string NOT_IN_PARENT_RANGE = "NOT_IN_PARENT_RANGE";
    private const string SUBNET_OVERLAP = "SUBNET_OVERLAP";
    private const string REQUIRED_FIELD_MISSING = "REQUIRED_FIELD_MISSING";
    private const string CHILD_SUBNET_OUTSIDE_RANGE = "CHILD_SUBNET_OUTSIDE_RANGE";
    private const string INVALID_CIDR_CHANGE = "INVALID_CIDR_CHANGE";
    private const string PARENT_HAS_HOST_IPS = "PARENT_HAS_HOST_IPS";

    /// <inheritdoc />
    public ValidationResult ValidateSubnetContainment(string childNetwork, int childCidr, string parentNetwork, int parentCidr)
    {
        ValidationResult result = new();

        // Child subnet must be contained within the parent subnet
        if (!ipUtilityService.IsSubnetContainedInParent(
            childNetwork, childCidr, parentNetwork, parentCidr))
        {
            result.AddError(NOT_IN_PARENT_RANGE,
                "Child subnet must be contained within the parent subnet range");
        }

        // Child CIDR must be larger than parent CIDR (smaller subnet)
        if (childCidr <= parentCidr)
        {
            result.AddError(INVALID_CIDR_HIERARCHY,
                "Child subnet CIDR must be larger than parent subnet CIDR (representing a smaller subnet)");
        }

        return result;
    }

    /// <inheritdoc />
    public ValidationResult ValidateSubnetFormat(string networkAddress, int cidr)
    {
        ValidationResult result = new();

        // Validate CIDR range
        if (cidr is < 0 or > 32)
        {
            result.AddError(INVALID_CIDR_VALUE, "CIDR must be between 0 and 32");
        }

        // Validate network address format (basic check, the utility will do more thorough validation)
        try
        {
            System.Net.IPAddress.Parse(networkAddress);
        }
        catch
        {
            result.AddError(INVALID_NETWORK_FORMAT, "Invalid IP address format");
            return result;
        }

        // Validate network address alignment with CIDR
        if (!ipUtilityService.IsValidSubnet(networkAddress, cidr))
        {
            result.AddError(NETWORK_CIDR_MISMATCH,
                "Network address is not valid for the given CIDR. The network address must align with the subnet boundary.");
        }

        return result;
    }

    /// <inheritdoc />
    public ValidationResult ValidateSiblingOverlap(string networkAddress, int cidr, IEnumerable<Subnet> siblings)
    {
        ValidationResult result = new();

        foreach (Subnet sibling in siblings)
        {
            // Check for identical subnets first (exact same network address and CIDR)
            if (sibling.NetworkAddress == networkAddress && sibling.Cidr == cidr)
            {
                result.AddError(SUBNET_OVERLAP,
                    $"Subnet is identical to existing subnet: {sibling.Name} ({sibling.NetworkAddress}/{sibling.Cidr})");
                break; // One overlap error is enough
            }

            // Check for containment in either direction
            bool childContainsSibling = ipUtilityService.IsSubnetContainedInParent(
                sibling.NetworkAddress, sibling.Cidr,
                networkAddress, cidr);

            bool siblingContainsChild = ipUtilityService.IsSubnetContainedInParent(
                networkAddress, cidr,
                sibling.NetworkAddress, sibling.Cidr);

            if (childContainsSibling || siblingContainsChild)
            {
                result.AddError(SUBNET_OVERLAP,
                    $"Subnet overlaps with existing subnet: {sibling.Name} ({sibling.NetworkAddress}/{sibling.Cidr})");
                break; // One overlap error is enough
            }
        }

        return result;
    }

    /// <inheritdoc />
    public ValidationResult ValidateSubnetCidrChange(
        int subnetId,
        string networkAddress,
        int originalCidr,
        int newCidr,
        Subnet? parentSubnet = null,
        IEnumerable<Subnet>? siblings = null,
        IEnumerable<Subnet>? children = null,
        IEnumerable<Subnet>? allOtherSubnets = null)
    {
        ValidationResult result = new();

        // Basic validation - check if CIDR value is valid
        if (newCidr is < 0 or > 32)
        {
            result.AddError(INVALID_CIDR_VALUE, "CIDR must be between 0 and 32");
            return result;
        }

        // Validate that new CIDR creates a valid subnet with the network address
        ValidationResult formatResult = ValidateSubnetFormat(networkAddress, newCidr);
        if (!formatResult.IsValid)
        {
            foreach (ValidationError error in formatResult.Errors)
            {
                result.AddError(error.Code, error.Message);
            }

            return result;
        }

        // If CIDR is not changing, no further validation needed
        if (originalCidr == newCidr)
        {
            return result;
        }

        // Different validation paths based on whether we're making subnet larger or smaller
        if (newCidr < originalCidr) // Making subnet larger (decreasing CIDR)
        {
            // Validate parent containment if parent exists
            if (parentSubnet != null)
            {
                // Check that the new subnet is still properly contained in the parent
                ValidationResult containmentResult = ValidateSubnetContainment(
                    networkAddress, newCidr,
                    parentSubnet.NetworkAddress, parentSubnet.Cidr);

                if (!containmentResult.IsValid)
                {
                    foreach (ValidationError error in containmentResult.Errors)
                    {
                        result.AddError(error.Code, error.Message);
                    }

                    // Add a more specific error message for the CIDR change context
                    result.AddError(INVALID_CIDR_CHANGE,
                        $"Decreasing CIDR to /{newCidr} would make this subnet too large to fit within its parent subnet " +
                        $"({parentSubnet.NetworkAddress}/{parentSubnet.Cidr})");
                }
            }

            // Validate no overlap with siblings
            if (siblings != null && siblings.Any())
            {
                ValidationResult overlapResult = ValidateSiblingOverlap(
                    networkAddress, newCidr, siblings);

                if (!overlapResult.IsValid)
                {
                    foreach (ValidationError error in overlapResult.Errors)
                    {
                        result.AddError(error.Code, error.Message);
                    }

                    // Add a more specific error message
                    result.AddError(INVALID_CIDR_CHANGE,
                        $"Decreasing CIDR to /{newCidr} would cause overlap with sibling subnet(s)");
                }
            }

            // Check for overlaps with any other subnets in the system
            if (allOtherSubnets != null && allOtherSubnets.Any())
            {
                // Ancestors and descendants are *supposed* to contain / be contained by this subnet,
                // and expanding it cannot break either relation: containment in the direct parent is
                // validated above and is transitive, so the subnet still fits every ancestor, and a
                // larger subnet still holds every descendant it already held. Only unrelated subnets
                // count as overlaps here - skipping just the direct relatives used to reject every
                // CIDR decrease made on a subnet with a grandparent or grandchildren.
                HashSet<int> hierarchyRelatedIds = CollectHierarchyRelatedIds(subnetId, parentSubnet, allOtherSubnets);

                foreach (Subnet otherSubnet in allOtherSubnets)
                {
                    // Skip siblings since we already checked them above
                    if (siblings != null && siblings.Contains(otherSubnet))
                    {
                        continue;
                    }

                    // Skip this subnet's own ancestors and descendants (see above)
                    if (hierarchyRelatedIds.Contains(otherSubnet.Id))
                    {
                        continue;
                    }

                    // Check if the expanded subnet would overlap with this other subnet
                    bool otherSubnetContainsThis = ipUtilityService.IsSubnetContainedInParent(
                        networkAddress, newCidr,
                        otherSubnet.NetworkAddress, otherSubnet.Cidr);

                    bool thisContainsOtherSubnet = ipUtilityService.IsSubnetContainedInParent(
                        otherSubnet.NetworkAddress, otherSubnet.Cidr,
                        networkAddress, newCidr);

                    // If there's any overlap, the subnets would conflict
                    if (otherSubnetContainsThis || thisContainsOtherSubnet)
                    {
                        result.AddError(SUBNET_OVERLAP,
                            $"Expanding to {networkAddress}/{newCidr} would conflict with existing subnet: " +
                            $"{otherSubnet.Name} ({otherSubnet.NetworkAddress}/{otherSubnet.Cidr})");
                    }
                }
            }
        }
        else // Making subnet smaller (increasing CIDR)
        {
            // Validate that all children still fit within the subnet
            if (children != null && children.Any())
            {
                foreach (Subnet child in children)
                {
                    // Check if child is still contained within the subnet with new CIDR
                    if (!ipUtilityService.IsSubnetContainedInParent(
                        child.NetworkAddress, child.Cidr,
                        networkAddress, newCidr))
                    {
                        result.AddError(CHILD_SUBNET_OUTSIDE_RANGE,
                            $"Child subnet {child.Name} ({child.NetworkAddress}/{child.Cidr}) would no longer " +
                            $"fit within this subnet if CIDR is increased to /{newCidr}");
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Collects the ids of every ancestor and every descendant of <paramref name="subnetId"/>, whose
    /// containment relationships with it are the hierarchy itself rather than overlaps. The subnet
    /// being validated is not in <paramref name="allOtherSubnets"/>, so ancestors are walked from
    /// <paramref name="parentSubnet"/> upwards; descendants are found by walking child links down.
    /// </summary>
    private static HashSet<int> CollectHierarchyRelatedIds(
        int subnetId,
        Subnet? parentSubnet,
        IEnumerable<Subnet> allOtherSubnets)
    {
        List<Subnet> all = [.. allOtherSubnets];
        HashSet<int> related = [];

        // Ancestors: start at the direct parent and follow parent links up. Adding to the set is the
        // loop condition, so a cycle in the data terminates instead of spinning.
        Dictionary<int, Subnet> byId = [];
        foreach (Subnet subnet in all)
        {
            byId.TryAdd(subnet.Id, subnet);
        }

        Subnet? ancestor = parentSubnet;
        while (ancestor != null && related.Add(ancestor.Id))
        {
            ancestor = ancestor.ParentSubnetId.HasValue && byId.TryGetValue(ancestor.ParentSubnetId.Value, out Subnet? next)
                ? next
                : null;
        }

        // Descendants: breadth-first down the child links from the subnet being validated.
        Queue<int> queue = new();
        queue.Enqueue(subnetId);
        while (queue.Count > 0)
        {
            int currentId = queue.Dequeue();
            foreach (Subnet child in all.Where(s => s.ParentSubnetId == currentId))
            {
                if (related.Add(child.Id))
                {
                    queue.Enqueue(child.Id);
                }
            }
        }

        return related;
    }

    /// <inheritdoc />
    public ValidationResult ValidateParentCanHaveChildSubnets(int parentId, IEnumerable<HostIpAssignment>? hostIps = null)
    {
        ValidationResult result = new();

        // Check if the parent subnet has host IP assignments
        if (hostIps != null && hostIps.Any())
        {
            result.AddError(PARENT_HAS_HOST_IPS,
                "Cannot create child subnets in a subnet that has host IP assignments. A subnet can have either child subnets or host IPs, but not both.");
        }

        return result;
    }
}
