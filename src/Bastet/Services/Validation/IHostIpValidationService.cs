using Bastet.Models.DTOs;

namespace Bastet.Services.Validation;

public interface IHostIpValidationService
{

    ValidationResult ValidateNewHostIp(string ip, int subnetId);

    ValidationResult ValidateHostIpUpdate(string originalIp, UpdateHostIpDto dto, byte[] rowVersion);

    ValidationResult ValidateHostIpDeletion(string ip);

    ValidationResult ValidateSubnetCanContainHostIp(int subnetId);

    ValidationResult ValidateSubnetCanBeFullyAllocated(int subnetId);

    ValidationResult ValidateIpIsWithinSubnet(string ip, string networkAddress, int cidr);

    ValidationResult ValidateSubnetCidrChangeWithHostIps(int subnetId, string networkAddress,
                                                        int originalCidr, int newCidr);
}
