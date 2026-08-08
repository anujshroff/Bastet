using Bastet.Models;

namespace Bastet.Services.Validation;

public interface ISubnetValidationService
{

    ValidationResult ValidateSubnetContainment(string childNetwork, int childCidr, string parentNetwork, int parentCidr);

    ValidationResult ValidateSubnetFormat(string networkAddress, int cidr);

    ValidationResult ValidateSiblingOverlap(string networkAddress, int cidr, IEnumerable<Subnet> siblings);

    ValidationResult ValidateSubnetCidrChange(
        int subnetId,
        string networkAddress,
        int originalCidr,
        int newCidr,
        Subnet? parentSubnet = null,
        IEnumerable<Subnet>? siblings = null,
        IEnumerable<Subnet>? children = null,
        IEnumerable<Subnet>? allOtherSubnets = null);

    ValidationResult ValidateParentCanHaveChildSubnets(int parentId, IEnumerable<HostIpAssignment>? hostIps = null);
}
