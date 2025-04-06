using Bastet.Data;
using Bastet.Models;
using Bastet.Models.DTOs;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Bastet.Services.Validation;

/// <summary>
/// Service for validating host IP assignment operations
/// </summary>
public class HostIpValidationService(IIpUtilityService ipUtilityService, BastetDbContext context) : IHostIpValidationService
{

    // Error codes
    private const string IP_OUTSIDE_SUBNET_RANGE = "IP_OUTSIDE_SUBNET_RANGE";
    private const string SUBNET_HAS_CHILDREN = "SUBNET_HAS_CHILDREN";
    private const string SUBNET_HAS_HOST_IPS = "SUBNET_HAS_HOST_IPS";
    private const string SUBNET_FULLY_ALLOCATED = "SUBNET_FULLY_ALLOCATED";
    private const string IP_ALREADY_ASSIGNED = "IP_ALREADY_ASSIGNED";
    private const string INVALID_IP_FORMAT = "INVALID_IP_FORMAT";
    private const string SUBNET_NOT_FOUND = "SUBNET_NOT_FOUND";
    private const string HOST_IP_NOT_FOUND = "HOST_IP_NOT_FOUND";
    private const string CONCURRENCY_CONFLICT = "CONCURRENCY_CONFLICT";
    private const string CIDR_CHANGE_INVALID = "CIDR_CHANGE_INVALID";

    /// <inheritdoc />
    public ValidationResult ValidateNewHostIp(string ip, int subnetId)
    {
        ValidationResult result = new();

        // Validate IP format
        if (!IsValidIpFormat(ip))
        {
            result.AddError(INVALID_IP_FORMAT, "Invalid IPv4 address format");
            return result;
        }

        // Get subnet
        Subnet? subnet = context.Subnets
            .Include(s => s.ChildSubnets)
            .Include(s => s.HostIpAssignments)
            .FirstOrDefault(s => s.Id == subnetId);

        if (subnet == null)
        {
            result.AddError(SUBNET_NOT_FOUND, "Subnet not found");
            return result;
        }

        // Validate subnet can have host IPs
        ValidationResult subnetValidation = ValidateSubnetCanContainHostIp(subnetId);
        if (!subnetValidation.IsValid)
        {
            foreach (ValidationError error in subnetValidation.Errors)
            {
                result.AddError(error.Code, error.Message);
            }

            return result;
        }

        // Validate IP is within subnet range
        ValidationResult ipRangeValidation = ValidateIpIsWithinSubnet(ip, subnet.NetworkAddress, subnet.Cidr);
        if (!ipRangeValidation.IsValid)
        {
            foreach (ValidationError error in ipRangeValidation.Errors)
            {
                result.AddError(error.Code, error.Message);
            }

            return result;
        }

        // Check if IP is already assigned
        if (context.HostIpAssignments.Any(h => h.IP == ip))
        {
            result.AddError(IP_ALREADY_ASSIGNED, "This IP address is already assigned");
        }

        return result;
    }

    /// <inheritdoc />
    public ValidationResult ValidateHostIpUpdate(string originalIp, UpdateHostIpDto dto, byte[] rowVersion)
    {
        ValidationResult result = new();

        // Find the existing host IP assignment
        HostIpAssignment? hostIp = context.HostIpAssignments
            .FirstOrDefault(h => h.IP == originalIp);

        if (hostIp == null)
        {
            result.AddError(HOST_IP_NOT_FOUND, "Host IP assignment not found");
            return result;
        }

        // Check for concurrency conflict
        if (!CompareRowVersions(hostIp.RowVersion, rowVersion))
        {
            result.AddError(CONCURRENCY_CONFLICT,
                "The host IP assignment has been modified by another user. Please reload and try again.");
            return result;
        }

        // If IP is being changed, validate it
        if (dto.IP != originalIp)
        {
            // Validate IP format
            if (!IsValidIpFormat(dto.IP))
            {
                result.AddError(INVALID_IP_FORMAT, "Invalid IPv4 address format");
                return result;
            }

            // Check if new IP is already assigned
            if (context.HostIpAssignments.Any(h => h.IP == dto.IP))
            {
                result.AddError(IP_ALREADY_ASSIGNED, "This IP address is already assigned");
                return result;
            }

            // Validate IP is within subnet range
            Subnet? subnet = context.Subnets.Find(hostIp.SubnetId);
            if (subnet != null)
            {
                ValidationResult ipRangeValidation = ValidateIpIsWithinSubnet(dto.IP, subnet.NetworkAddress, subnet.Cidr);
                if (!ipRangeValidation.IsValid)
                {
                    foreach (ValidationError error in ipRangeValidation.Errors)
                    {
                        result.AddError(error.Code, error.Message);
                    }

                    return result;
                }
            }
        }

        return result;
    }

    /// <inheritdoc />
    public ValidationResult ValidateHostIpDeletion(string ip)
    {
        ValidationResult result = new();

        // Check if host IP exists
        HostIpAssignment? hostIp = context.HostIpAssignments
            .FirstOrDefault(h => h.IP == ip);

        if (hostIp == null)
        {
            result.AddError(HOST_IP_NOT_FOUND, "Host IP assignment not found");
        }

        return result;
    }

    /// <inheritdoc />
    public ValidationResult ValidateSubnetCanContainHostIp(int subnetId)
    {
        ValidationResult result = new();

        // Get subnet with its relationships
        Subnet? subnet = context.Subnets
            .Include(s => s.ChildSubnets)
            .FirstOrDefault(s => s.Id == subnetId);

        if (subnet == null)
        {
            result.AddError(SUBNET_NOT_FOUND, "Subnet not found");
            return result;
        }

        // Check if subnet has child subnets
        if (subnet.ChildSubnets.Count > 0)
        {
            result.AddError(SUBNET_HAS_CHILDREN,
                "Cannot add host IP assignments to a subnet that has child subnets");
            return result;
        }

        // Check if subnet is fully allocated
        if (subnet.IsFullyAllocated)
        {
            result.AddError(SUBNET_FULLY_ALLOCATED,
                "Cannot add host IP assignments to a subnet that is marked as fully allocated");
        }

        return result;
    }

    /// <inheritdoc />
    public ValidationResult ValidateSubnetCanBeFullyAllocated(int subnetId)
    {
        ValidationResult result = new();

        // Get subnet with its relationships
        Subnet? subnet = context.Subnets
            .Include(s => s.ChildSubnets)
            .Include(s => s.HostIpAssignments)
            .FirstOrDefault(s => s.Id == subnetId);

        if (subnet == null)
        {
            result.AddError(SUBNET_NOT_FOUND, "Subnet not found");
            return result;
        }

        // Check if subnet has child subnets
        if (subnet.ChildSubnets.Count > 0)
        {
            result.AddError(SUBNET_HAS_CHILDREN,
                "Cannot mark a subnet as fully allocated if it has child subnets");
            return result;
        }

        // Check if subnet has host IP assignments
        if (subnet.HostIpAssignments.Count > 0)
        {
            result.AddError(SUBNET_HAS_HOST_IPS,
                "Cannot mark a subnet as fully allocated if it already has host IP assignments");
        }

        return result;
    }

    /// <inheritdoc />
    public ValidationResult ValidateIpIsWithinSubnet(string ip, string networkAddress, int cidr)
    {
        ValidationResult result = new();

        // Validate IP format
        if (!IsValidIpFormat(ip))
        {
            result.AddError(INVALID_IP_FORMAT, "Invalid IPv4 address format");
            return result;
        }

        // Check if IP is within subnet range
        if (!ipUtilityService.IsIpInSubnet(ip, networkAddress, cidr))
        {
            result.AddError(IP_OUTSIDE_SUBNET_RANGE,
                $"IP address {ip} is outside the subnet range {networkAddress}/{cidr}");
        }

        return result;
    }

    /// <inheritdoc />
    public ValidationResult ValidateSubnetCidrChangeWithHostIps(int subnetId, string networkAddress,
                                                              int originalCidr, int newCidr)
    {
        ValidationResult result = new();

        // Get subnet with its host IP assignments
        Subnet? subnet = context.Subnets
            .Include(s => s.HostIpAssignments)
            .FirstOrDefault(s => s.Id == subnetId);

        if (subnet == null)
        {
            result.AddError(SUBNET_NOT_FOUND, "Subnet not found");
            return result;
        }

        // If subnet has no host IPs, no further validation needed
        if (subnet.HostIpAssignments.Count == 0)
        {
            return result;
        }

        // If increasing CIDR (making subnet smaller), check if all host IPs still fit
        if (newCidr > originalCidr)
        {
            foreach (HostIpAssignment hostIp in subnet.HostIpAssignments)
            {
                if (!ipUtilityService.IsIpInSubnet(hostIp.IP, networkAddress, newCidr))
                {
                    result.AddError(CIDR_CHANGE_INVALID,
                        $"Cannot increase CIDR to /{newCidr} as host IP {hostIp.IP} would fall outside the subnet range");
                    break; // One failure is enough to invalidate the change
                }
            }
        }

        return result;
    }

    // Helper method to validate IP format
    private static bool IsValidIpFormat(string ip) => IPAddress.TryParse(ip, out IPAddress? parsedIp) &&
               parsedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;

    // Helper method to compare row versions
    private static bool CompareRowVersions(byte[]? current, byte[]? provided) => current != null && provided != null && current.Length == provided.Length && current.SequenceEqual(provided);
}
