using Bastet.Models;
using Bastet.Models.DTOs;

namespace Bastet.Services.Division;

/// <summary>
/// Service interface for subnet division operations
/// </summary>
public interface ISubnetDivisionService
{
    /// <summary>
    /// Divide a subnet into multiple child subnets
    /// </summary>
    /// <param name="parentSubnet">The parent subnet to divide</param>
    /// <param name="divisionDto">The division parameters</param>
    /// <param name="existingChildren">Existing child subnets to check for overlaps</param>
    /// <returns>A list of created subnet entities</returns>
    Task<List<Subnet>> DivideSubnetAsync(Subnet parentSubnet, SubnetDivisionDto divisionDto, IEnumerable<Subnet>? existingChildren = null);

    /// <summary>
    /// Get possible divisions for a subnet
    /// </summary>
    /// <param name="parentSubnet">The parent subnet</param>
    /// <param name="targetCidr">The target CIDR for the divisions</param>
    /// <returns>A list of possible subnet calculations</returns>
    List<SubnetCalculation> GetPossibleDivisions(Subnet parentSubnet, int targetCidr);

    /// <summary>
    /// Check if a subnet can be divided to the specified target CIDR
    /// </summary>
    /// <param name="subnet">The subnet to check</param>
    /// <param name="targetCidr">The target CIDR</param>
    /// <returns>True if the subnet can be divided, false otherwise</returns>
    bool CanSubnetBeDivided(Subnet subnet, int targetCidr);

    /// <summary>
    /// Generate a name for a child subnet
    /// </summary>
    /// <param name="parentSubnet">The parent subnet</param>
    /// <param name="childNetwork">The child network address</param>
    /// <param name="childCidr">The child CIDR</param>
    /// <param name="namePrefix">Optional name prefix</param>
    /// <param name="index">Index of the child subnet</param>
    /// <returns>A generated name for the child subnet</returns>
    string GenerateSubnetName(Subnet parentSubnet, string childNetwork, int childCidr, string? namePrefix = null, int index = 0);

    /// <summary>
    /// Generate a description for a child subnet
    /// </summary>
    /// <param name="parentSubnet">The parent subnet</param>
    /// <param name="childNetwork">The child network address</param>
    /// <param name="childCidr">The child CIDR</param>
    /// <param name="descriptionTemplate">Optional description template</param>
    /// <param name="index">Index of the child subnet</param>
    /// <returns>A generated description for the child subnet</returns>
    string? GenerateSubnetDescription(Subnet parentSubnet, string childNetwork, int childCidr, string? descriptionTemplate = null, int index = 0);
}
