using Bastet.Models;
using Bastet.Models.DTOs;

namespace Bastet.Services.Validation;

/// <summary>
/// Defines methods for validating host IP assignment operations
/// </summary>
public interface IHostIpValidationService
{
    /// <summary>
    /// Validates creation of a new host IP assignment
    /// </summary>
    /// <param name="ip">The IP address to assign</param>
    /// <param name="subnetId">The subnet ID to assign the IP to</param>
    /// <returns>Validation result indicating success or errors</returns>
    ValidationResult ValidateNewHostIp(string ip, int subnetId);

    /// <summary>
    /// Validates updating an existing host IP assignment
    /// </summary>
    /// <param name="originalIp">The original IP address</param>
    /// <param name="dto">The host IP update data</param>
    /// <param name="rowVersion">Current rowVersion for concurrency control</param>
    /// <returns>Validation result indicating success or errors</returns>
    ValidationResult ValidateHostIpUpdate(string originalIp, UpdateHostIpDto dto, byte[] rowVersion);

    /// <summary>
    /// Validates deletion of a host IP assignment
    /// </summary>
    /// <param name="ip">The IP address to delete</param>
    /// <returns>Validation result indicating success or errors</returns>
    ValidationResult ValidateHostIpDeletion(string ip);

    /// <summary>
    /// Validates that a subnet can have host IP assignments added to it
    /// </summary>
    /// <param name="subnetId">The subnet ID to check</param>
    /// <returns>Validation result indicating if the subnet can have host IPs</returns>
    ValidationResult ValidateSubnetCanContainHostIp(int subnetId);

    /// <summary>
    /// Validates that a subnet can be marked as fully allocated
    /// </summary>
    /// <param name="subnetId">The subnet ID to check</param>
    /// <returns>Validation result indicating if the subnet can be fully allocated</returns>
    ValidationResult ValidateSubnetCanBeFullyAllocated(int subnetId);

    /// <summary>
    /// Validates that an IP address is within a subnet's range
    /// </summary>
    /// <param name="ip">The IP address to check</param>
    /// <param name="networkAddress">The subnet's network address</param>
    /// <param name="cidr">The subnet's CIDR</param>
    /// <returns>Validation result indicating if the IP is within the subnet</returns>
    ValidationResult ValidateIpIsWithinSubnet(string ip, string networkAddress, int cidr);

    /// <summary>
    /// Validates that a subnet's CIDR change would not affect existing host IP assignments
    /// </summary>
    /// <param name="subnetId">The subnet ID being modified</param>
    /// <param name="networkAddress">The subnet's network address</param>
    /// <param name="originalCidr">The original CIDR value</param>
    /// <param name="newCidr">The new CIDR value</param>
    /// <returns>Validation result indicating if the CIDR change is valid</returns>
    ValidationResult ValidateSubnetCidrChangeWithHostIps(int subnetId, string networkAddress,
                                                        int originalCidr, int newCidr);
}
