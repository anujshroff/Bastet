using Bastet.Models;
using Bastet.Models.DTOs;

namespace Bastet.Services.Validation;

/// <summary>
/// Defines methods for validating subnet operations
/// </summary>
public interface ISubnetValidationService
{
    /// <summary>
    /// Validates a new subnet creation
    /// </summary>
    /// <param name="subnetDto">The subnet creation data</param>
    /// <param name="parentSubnet">The parent subnet, if any</param>
    /// <param name="siblings">Sibling subnets, if any</param>
    ValidationResult ValidateNewSubnet(CreateSubnetDto subnetDto, Subnet? parentSubnet = null, IEnumerable<Subnet>? siblings = null);

    /// <summary>
    /// Validates a subnet update
    /// </summary>
    /// <param name="existingSubnet">The existing subnet being updated</param>
    /// <param name="updateDto">The update data</param>
    ValidationResult ValidateSubnetUpdate(Subnet existingSubnet, UpdateSubnetDto updateDto);

    /// <summary>
    /// Validates subnet deletion
    /// </summary>
    /// <param name="subnet">The subnet to delete</param>
    ValidationResult ValidateSubnetDeletion(Subnet subnet);

    /// <summary>
    /// Validates that a subnet is properly contained within a parent subnet
    /// </summary>
    /// <param name="childNetwork">The child network address</param>
    /// <param name="childCidr">The child CIDR</param>
    /// <param name="parentNetwork">The parent network address</param>
    /// <param name="parentCidr">The parent CIDR</param>
    ValidationResult ValidateSubnetContainment(string childNetwork, int childCidr, string parentNetwork, int parentCidr);

    /// <summary>
    /// Validates that a subnet has the correct format
    /// </summary>
    /// <param name="networkAddress">The network address</param>
    /// <param name="cidr">The CIDR value</param>
    ValidationResult ValidateSubnetFormat(string networkAddress, int cidr);

    /// <summary>
    /// Validates that a subnet doesn't overlap with its siblings
    /// </summary>
    /// <param name="networkAddress">The network address</param>
    /// <param name="cidr">The CIDR value</param>
    /// <param name="siblings">The sibling subnets</param>
    ValidationResult ValidateSiblingOverlap(string networkAddress, int cidr, IEnumerable<Subnet> siblings);
}
