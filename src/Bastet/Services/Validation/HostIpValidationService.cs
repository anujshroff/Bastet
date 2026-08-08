using Bastet.Data;
using Bastet.Models;
using Bastet.Models.DTOs;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Bastet.Services.Validation;

public class HostIpValidationService(IIpUtilityService ipUtilityService, BastetDbContext context) : IHostIpValidationService
{

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
    private const string NETWORK_ADDRESS_RESERVED = "NETWORK_ADDRESS_RESERVED";
    private const string BROADCAST_ADDRESS_RESERVED = "BROADCAST_ADDRESS_RESERVED";

    public ValidationResult ValidateNewHostIp(string ip, int subnetId)
    {
        ValidationResult result = new();

        if (!IsValidIpFormat(ip))
        {
            result.AddError(INVALID_IP_FORMAT, "Invalid IPv4 address format");
            return result;
        }

        Subnet? subnet = context.Subnets
            .Include(s => s.ChildSubnets)
            .Include(s => s.HostIpAssignments)
            .FirstOrDefault(s => s.Id == subnetId);

        if (subnet == null)
        {
            result.AddError(SUBNET_NOT_FOUND, "Subnet not found");
            return result;
        }

        ValidationResult subnetValidation = ValidateSubnetCanContainHostIp(subnetId);
        if (!subnetValidation.IsValid)
        {
            foreach (ValidationError error in subnetValidation.Errors)
            {
                result.AddError(error.Code, error.Message);
            }

            return result;
        }

        if (subnet.Cidr < 31)
        {

            if (ip == subnet.NetworkAddress)
            {
                result.AddError(NETWORK_ADDRESS_RESERVED, "Cannot assign the network address as a host IP");
                return result;
            }

            string broadcastAddress = ipUtilityService.CalculateBroadcastAddress(subnet.NetworkAddress, subnet.Cidr);
            if (ip == broadcastAddress)
            {
                result.AddError(BROADCAST_ADDRESS_RESERVED, "Cannot assign the broadcast address as a host IP");
                return result;
            }
        }

        ValidationResult ipRangeValidation = ValidateIpIsWithinSubnet(ip, subnet.NetworkAddress, subnet.Cidr);
        if (!ipRangeValidation.IsValid)
        {
            foreach (ValidationError error in ipRangeValidation.Errors)
            {
                result.AddError(error.Code, error.Message);
            }

            return result;
        }

        if (context.HostIpAssignments.Any(h => h.IP == ip))
        {
            result.AddError(IP_ALREADY_ASSIGNED, "This IP address is already assigned");
        }

        return result;
    }

    public ValidationResult ValidateHostIpUpdate(string originalIp, UpdateHostIpDto dto, byte[] rowVersion)
    {
        ValidationResult result = new();

        HostIpAssignment? hostIp = context.HostIpAssignments
            .FirstOrDefault(h => h.IP == originalIp);

        if (hostIp == null)
        {
            result.AddError(HOST_IP_NOT_FOUND, "Host IP assignment not found");
            return result;
        }

        if (!CompareRowVersions(hostIp.RowVersion, rowVersion))
        {
            result.AddError(CONCURRENCY_CONFLICT,
                "The host IP assignment has been modified by another user. Please reload and try again.");
            return result;
        }

        if (dto.IP != originalIp)
        {

            if (!IsValidIpFormat(dto.IP))
            {
                result.AddError(INVALID_IP_FORMAT, "Invalid IPv4 address format");
                return result;
            }

            if (context.HostIpAssignments.Any(h => h.IP == dto.IP))
            {
                result.AddError(IP_ALREADY_ASSIGNED, "This IP address is already assigned");
                return result;
            }

            Subnet? subnet = context.Subnets.Find(hostIp.SubnetId);
            if (subnet != null)
            {

                if (dto.IP == subnet.NetworkAddress)
                {
                    result.AddError(NETWORK_ADDRESS_RESERVED, "Cannot assign the network address as a host IP");
                    return result;
                }

                string broadcastAddress = ipUtilityService.CalculateBroadcastAddress(subnet.NetworkAddress, subnet.Cidr);
                if (dto.IP == broadcastAddress)
                {
                    result.AddError(BROADCAST_ADDRESS_RESERVED, "Cannot assign the broadcast address as a host IP");
                    return result;
                }

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

    public ValidationResult ValidateHostIpDeletion(string ip)
    {
        ValidationResult result = new();

        HostIpAssignment? hostIp = context.HostIpAssignments
            .FirstOrDefault(h => h.IP == ip);

        if (hostIp == null)
        {
            result.AddError(HOST_IP_NOT_FOUND, "Host IP assignment not found");
        }

        return result;
    }

    public ValidationResult ValidateSubnetCanContainHostIp(int subnetId)
    {
        ValidationResult result = new();

        Subnet? subnet = context.Subnets
            .Include(s => s.ChildSubnets)
            .FirstOrDefault(s => s.Id == subnetId);

        if (subnet == null)
        {
            result.AddError(SUBNET_NOT_FOUND, "Subnet not found");
            return result;
        }

        if (subnet.ChildSubnets.Count > 0)
        {
            result.AddError(SUBNET_HAS_CHILDREN,
                "Cannot add host IP assignments to a subnet that has child subnets");
            return result;
        }

        if (subnet.IsFullyAllocated)
        {
            result.AddError(SUBNET_FULLY_ALLOCATED,
                "Cannot add host IP assignments to a subnet that is marked as fully allocated");
        }

        return result;
    }

    public ValidationResult ValidateSubnetCanBeFullyAllocated(int subnetId)
    {
        ValidationResult result = new();

        Subnet? subnet = context.Subnets
            .Include(s => s.ChildSubnets)
            .Include(s => s.HostIpAssignments)
            .FirstOrDefault(s => s.Id == subnetId);

        if (subnet == null)
        {
            result.AddError(SUBNET_NOT_FOUND, "Subnet not found");
            return result;
        }

        if (subnet.ChildSubnets.Count > 0)
        {
            result.AddError(SUBNET_HAS_CHILDREN,
                "Cannot mark a subnet as fully allocated if it has child subnets");
            return result;
        }

        if (subnet.HostIpAssignments.Count > 0)
        {
            result.AddError(SUBNET_HAS_HOST_IPS,
                "Cannot mark a subnet as fully allocated if it already has host IP assignments");
        }

        return result;
    }

    public ValidationResult ValidateIpIsWithinSubnet(string ip, string networkAddress, int cidr)
    {
        ValidationResult result = new();

        if (!IsValidIpFormat(ip))
        {
            result.AddError(INVALID_IP_FORMAT, "Invalid IPv4 address format");
            return result;
        }

        if (!ipUtilityService.IsIpInSubnet(ip, networkAddress, cidr))
        {
            result.AddError(IP_OUTSIDE_SUBNET_RANGE,
                $"IP address {ip} is outside the subnet range {networkAddress}/{cidr}");
        }

        return result;
    }

    public ValidationResult ValidateSubnetCidrChangeWithHostIps(int subnetId, string networkAddress,
                                                              int originalCidr, int newCidr)
    {
        ValidationResult result = new();

        Subnet? subnet = context.Subnets
            .Include(s => s.HostIpAssignments)
            .FirstOrDefault(s => s.Id == subnetId);

        if (subnet == null)
        {
            result.AddError(SUBNET_NOT_FOUND, "Subnet not found");
            return result;
        }

        if (subnet.HostIpAssignments.Count == 0)
        {
            return result;
        }

        if (newCidr > originalCidr)
        {

            string? newBroadcast = newCidr < 31
                ? ipUtilityService.CalculateBroadcastAddress(networkAddress, newCidr)
                : null;

            foreach (HostIpAssignment hostIp in subnet.HostIpAssignments)
            {
                if (!ipUtilityService.IsIpInSubnet(hostIp.IP, networkAddress, newCidr))
                {
                    result.AddError(CIDR_CHANGE_INVALID,
                        $"Cannot increase CIDR to /{newCidr} as host IP {hostIp.IP} would fall outside the subnet range");
                    break;
                }

                if (hostIp.IP == newBroadcast)
                {
                    result.AddError(CIDR_CHANGE_INVALID,
                        $"Cannot increase CIDR to /{newCidr} as host IP {hostIp.IP} would become the subnet's broadcast address");
                    break;
                }
            }
        }
        else if (newCidr < originalCidr
            && newCidr < 31
            && ipUtilityService.IsValidSubnet(networkAddress, newCidr))
        {

            foreach (HostIpAssignment hostIp in subnet.HostIpAssignments)
            {
                if (hostIp.IP == networkAddress)
                {
                    result.AddError(CIDR_CHANGE_INVALID,
                        $"Cannot decrease CIDR to /{newCidr} as host IP {hostIp.IP} would become the subnet's network address");
                    break;
                }
            }
        }

        return result;
    }

    private static bool IsValidIpFormat(string ip) => IPAddress.TryParse(ip, out IPAddress? parsedIp) &&
               parsedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;

    private static bool CompareRowVersions(byte[]? current, byte[]? provided) => current != null && provided != null && current.Length == provided.Length && current.SequenceEqual(provided);
}
