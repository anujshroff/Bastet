using Bastet.Models;

namespace Bastet.Services.Validation;

public class SubnetValidationService(IIpUtilityService ipUtilityService) : ISubnetValidationService
{

    private const string INVALID_NETWORK_FORMAT = "INVALID_NETWORK_FORMAT";
    private const string INVALID_CIDR_VALUE = "INVALID_CIDR_VALUE";
    private const string NETWORK_CIDR_MISMATCH = "NETWORK_CIDR_MISMATCH";
    private const string INVALID_CIDR_HIERARCHY = "INVALID_CIDR_HIERARCHY";
    private const string NOT_IN_PARENT_RANGE = "NOT_IN_PARENT_RANGE";
    private const string SUBNET_OVERLAP = "SUBNET_OVERLAP";
    private const string CHILD_SUBNET_OUTSIDE_RANGE = "CHILD_SUBNET_OUTSIDE_RANGE";
    private const string INVALID_CIDR_CHANGE = "INVALID_CIDR_CHANGE";
    private const string PARENT_HAS_HOST_IPS = "PARENT_HAS_HOST_IPS";

    public ValidationResult ValidateSubnetContainment(string childNetwork, int childCidr, string parentNetwork, int parentCidr)
    {
        ValidationResult result = new();

        if (!ipUtilityService.IsSubnetContainedInParent(
            childNetwork, childCidr, parentNetwork, parentCidr))
        {
            result.AddError(NOT_IN_PARENT_RANGE,
                "Child subnet must be contained within the parent subnet range");
        }

        if (childCidr <= parentCidr)
        {
            result.AddError(INVALID_CIDR_HIERARCHY,
                "Child subnet CIDR must be larger than parent subnet CIDR (representing a smaller subnet)");
        }

        return result;
    }

    public ValidationResult ValidateSubnetFormat(string networkAddress, int cidr)
    {
        ValidationResult result = new();

        if (cidr is < 0 or > 32)
        {
            result.AddError(INVALID_CIDR_VALUE, "CIDR must be between 0 and 32");
        }

        try
        {
            System.Net.IPAddress.Parse(networkAddress);
        }
        catch
        {
            result.AddError(INVALID_NETWORK_FORMAT, "Invalid IP address format");
            return result;
        }

        if (!ipUtilityService.IsValidSubnet(networkAddress, cidr))
        {
            result.AddError(NETWORK_CIDR_MISMATCH,
                "Network address is not valid for the given CIDR. The network address must align with the subnet boundary.");
        }

        return result;
    }

    public ValidationResult ValidateSiblingOverlap(string networkAddress, int cidr, IEnumerable<Subnet> siblings)
    {
        ValidationResult result = new();

        foreach (Subnet sibling in siblings)
        {

            if (sibling.NetworkAddress == networkAddress && sibling.Cidr == cidr)
            {
                result.AddError(SUBNET_OVERLAP,
                    $"Subnet is identical to existing subnet: {sibling.Name} ({sibling.NetworkAddress}/{sibling.Cidr})");
                break;
            }

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
                break;
            }
        }

        return result;
    }

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

        if (newCidr is < 0 or > 32)
        {
            result.AddError(INVALID_CIDR_VALUE, "CIDR must be between 0 and 32");
            return result;
        }

        ValidationResult formatResult = ValidateSubnetFormat(networkAddress, newCidr);
        if (!formatResult.IsValid)
        {
            foreach (ValidationError error in formatResult.Errors)
            {
                result.AddError(error.Code, error.Message);
            }

            return result;
        }

        if (originalCidr == newCidr)
        {
            return result;
        }

        if (newCidr < originalCidr)
        {

            if (parentSubnet != null)
            {

                ValidationResult containmentResult = ValidateSubnetContainment(
                    networkAddress, newCidr,
                    parentSubnet.NetworkAddress, parentSubnet.Cidr);

                if (!containmentResult.IsValid)
                {
                    foreach (ValidationError error in containmentResult.Errors)
                    {
                        result.AddError(error.Code, error.Message);
                    }

                    result.AddError(INVALID_CIDR_CHANGE,
                        $"Decreasing CIDR to /{newCidr} would make this subnet too large to fit within its parent subnet " +
                        $"({parentSubnet.NetworkAddress}/{parentSubnet.Cidr})");
                }
            }

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

                    result.AddError(INVALID_CIDR_CHANGE,
                        $"Decreasing CIDR to /{newCidr} would cause overlap with sibling subnet(s)");
                }
            }

            if (allOtherSubnets != null && allOtherSubnets.Any())
            {

                HashSet<int> hierarchyRelatedIds = CollectHierarchyRelatedIds(subnetId, parentSubnet, allOtherSubnets);

                foreach (Subnet otherSubnet in allOtherSubnets)
                {

                    if (siblings != null && siblings.Contains(otherSubnet))
                    {
                        continue;
                    }

                    if (hierarchyRelatedIds.Contains(otherSubnet.Id))
                    {
                        continue;
                    }

                    bool otherSubnetContainsThis = ipUtilityService.IsSubnetContainedInParent(
                        networkAddress, newCidr,
                        otherSubnet.NetworkAddress, otherSubnet.Cidr);

                    bool thisContainsOtherSubnet = ipUtilityService.IsSubnetContainedInParent(
                        otherSubnet.NetworkAddress, otherSubnet.Cidr,
                        networkAddress, newCidr);

                    if (otherSubnetContainsThis || thisContainsOtherSubnet)
                    {
                        result.AddError(SUBNET_OVERLAP,
                            $"Expanding to {networkAddress}/{newCidr} would conflict with existing subnet: " +
                            $"{otherSubnet.Name} ({otherSubnet.NetworkAddress}/{otherSubnet.Cidr})");
                    }
                }
            }
        }
        else
        {

            if (children != null && children.Any())
            {
                foreach (Subnet child in children)
                {

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

    private static HashSet<int> CollectHierarchyRelatedIds(
        int subnetId,
        Subnet? parentSubnet,
        IEnumerable<Subnet> allOtherSubnets)
    {
        List<Subnet> all = [.. allOtherSubnets];
        HashSet<int> related = [];

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

    public ValidationResult ValidateParentCanHaveChildSubnets(int parentId, IEnumerable<HostIpAssignment>? hostIps = null)
    {
        ValidationResult result = new();

        if (hostIps != null && hostIps.Any())
        {
            result.AddError(PARENT_HAS_HOST_IPS,
                "Cannot create child subnets in a subnet that has host IP assignments. A subnet can have either child subnets or host IPs, but not both.");
        }

        return result;
    }
}
