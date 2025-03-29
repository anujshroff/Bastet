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
    private const string SUBNET_HAS_CHILDREN = "SUBNET_HAS_CHILDREN";
    private const string REQUIRED_FIELD_MISSING = "REQUIRED_FIELD_MISSING";

    /// <inheritdoc />
    public ValidationResult ValidateNewSubnet(CreateSubnetDto subnetDto, Subnet? parentSubnet = null, IEnumerable<Subnet>? siblings = null)
    {
        ValidationResult result = new();

        // Basic field validation
        if (string.IsNullOrWhiteSpace(subnetDto.Name))
        {
            result.AddError(REQUIRED_FIELD_MISSING, "Name is required");
        }

        if (string.IsNullOrWhiteSpace(subnetDto.NetworkAddress))
        {
            result.AddError(REQUIRED_FIELD_MISSING, "Network address is required");
            return result; // Early return since other validations depend on network address
        }

        // Network and CIDR format validation
        ValidationResult formatResult = ValidateSubnetFormat(subnetDto.NetworkAddress, subnetDto.Cidr);
        if (!formatResult.IsValid)
        {
            foreach (ValidationError error in formatResult.Errors)
            {
                result.AddError(error.Code, error.Message);
            }

            return result; // Early return since other validations depend on valid format
        }

        // Parent-child relationship validation
        if (parentSubnet != null)
        {
            // CIDR hierarchy validation
            if (subnetDto.Cidr <= parentSubnet.Cidr)
            {
                result.AddError(INVALID_CIDR_HIERARCHY,
                    "Child subnet CIDR must be larger than parent subnet CIDR (representing a smaller subnet)");
            }

            // Containment validation
            ValidationResult containmentResult = ValidateSubnetContainment(
                subnetDto.NetworkAddress, subnetDto.Cidr,
                parentSubnet.NetworkAddress, parentSubnet.Cidr);

            if (!containmentResult.IsValid)
            {
                foreach (ValidationError error in containmentResult.Errors)
                {
                    result.AddError(error.Code, error.Message);
                }
            }

            // Sibling overlap validation
            if (siblings != null && siblings.Any())
            {
                ValidationResult overlapResult = ValidateSiblingOverlap(
                    subnetDto.NetworkAddress, subnetDto.Cidr, siblings);

                if (!overlapResult.IsValid)
                {
                    foreach (ValidationError error in overlapResult.Errors)
                    {
                        result.AddError(error.Code, error.Message);
                    }
                }
            }
        }

        return result;
    }

    /// <inheritdoc />
    public ValidationResult ValidateSubnetUpdate(Subnet existingSubnet, UpdateSubnetDto updateDto)
    {
        ValidationResult result = new();

        // Basic field validation
        if (string.IsNullOrWhiteSpace(updateDto.Name))
        {
            result.AddError(REQUIRED_FIELD_MISSING, "Name is required");
        }

        // We don't validate network properties as they cannot be changed in an update

        return result;
    }

    /// <inheritdoc />
    public ValidationResult ValidateSubnetDeletion(Subnet subnet)
    {
        ValidationResult result = new();

        // Prevent deletion if subnet has children
        if (subnet.ChildSubnets != null && subnet.ChildSubnets.Count != 0)
        {
            result.AddError(SUBNET_HAS_CHILDREN,
                "Cannot delete a subnet that has child subnets. Delete the children first.");
        }

        return result;
    }

    /// <inheritdoc />
    public ValidationResult ValidateSubnetContainment(string childNetwork, int childCidr, string parentNetwork, int parentCidr)
    {
        ValidationResult result = new();

        // Child CIDR must be larger than parent CIDR (smaller subnet)
        if (childCidr <= parentCidr)
        {
            result.AddError(INVALID_CIDR_HIERARCHY,
                "Child subnet CIDR must be larger than parent subnet CIDR (representing a smaller subnet)");
            return result;
        }

        // Child subnet must be contained within the parent subnet
        if (!ipUtilityService.IsSubnetContainedInParent(
            childNetwork, childCidr, parentNetwork, parentCidr))
        {
            result.AddError(NOT_IN_PARENT_RANGE,
                "Child subnet must be contained within the parent subnet range");
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
}
