using Bastet.Services.Security;
using System.ComponentModel.DataAnnotations;

namespace Bastet.Models.ViewModels;

public class CreateSubnetViewModel
{
    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, ErrorMessage = "Name cannot be longer than 100 characters")]
    [NoHtml(ErrorMessage = "HTML tags are not allowed in subnet names")]
    [SafeText(ErrorMessage = "Subnet name contains invalid characters")]
    [SanitizeName]
    [Display(Name = "Subnet Name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Network address is required")]
    [NetworkInput(RequireValidIp = true, ErrorMessage = "Invalid network address format")]
    [SanitizeNetworkInput]
    [Display(Name = "Network Address")]
    public string NetworkAddress { get; set; } = string.Empty;

    [Required(ErrorMessage = "CIDR notation is required")]
    [Range(0, 32, ErrorMessage = "CIDR must be between 0 and 32")]
    [Display(Name = "CIDR Notation")]
    public int Cidr { get; set; }

    [StringLength(1000, ErrorMessage = "Description cannot be longer than 1000 characters")]
    [NoHtml(ErrorMessage = "HTML tags are not allowed in descriptions")]
    [SanitizeDescription]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Parent Subnet")]
    public int? ParentSubnetId { get; set; }

    [StringLength(255, ErrorMessage = "Tags cannot be longer than 255 characters")]
    [Bastet.Services.Security.Tags(MaxTags = 10, MaxTagLength = 50, ErrorMessage = "Invalid tags format")]
    [SanitizeTags]
    [Display(Name = "Tags")]
    public string? Tags { get; set; }

    public List<SubnetViewModel> ParentSubnetOptions { get; set; } = [];

    public string CalculatedSubnetMask { get; set; } = string.Empty;
}

public class AzureImportSubnetViewModel : CreateSubnetViewModel
{

    public bool FullyEncompassesVNetPrefix { get; set; }

    public string? AzureResourceId { get; set; }
}

public class SubnetViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NetworkAddress { get; set; } = string.Empty;
    public int Cidr { get; set; }
}

public class SubnetTreeViewModel : SubnetViewModel
{
    public string? Description { get; set; }
    public string SubnetMask { get; set; } = string.Empty;
    public long UsableIpAddresses { get; set; }
    public int? ParentSubnetId { get; set; }
    public List<SubnetTreeViewModel> ChildSubnets { get; set; } = [];
}

public class SubnetDetailsViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NetworkAddress { get; set; } = string.Empty;
    public int Cidr { get; set; }
    public string? Description { get; set; }
    public string? Tags { get; set; }
    public string? AzureResourceId { get; set; }
    public int? ParentSubnetId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }

    public string SubnetMask { get; set; } = string.Empty;
    public string BroadcastAddress { get; set; } = string.Empty;
    public long TotalIpAddresses { get; set; }
    public long UsableIpAddresses { get; set; }

    public List<SubnetViewModel> ChildSubnets { get; set; } = [];

    public List<HostIpViewModel> HostIpAssignments { get; set; } = [];
    public bool IsFullyAllocated { get; set; }

    public bool CanAddHostIp => ChildSubnets.Count == 0 && !IsFullyAllocated;
    public bool CanAddChildSubnet => HostIpAssignments.Count == 0 && !IsFullyAllocated;

    public List<IPRange> UnallocatedRanges { get; set; } = [];

    public string? ParentSubnetName { get; set; }
    public string? ParentNetworkAddress { get; set; }
}
