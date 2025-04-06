using System.ComponentModel.DataAnnotations;

namespace Bastet.Models.ViewModels;

public class CreateSubnetViewModel
{
    [Required(ErrorMessage = "Name is required")]
    [StringLength(50, ErrorMessage = "Name cannot be longer than 50 characters")]
    [Display(Name = "Subnet Name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Network address is required")]
    [Display(Name = "Network Address")]
    public string NetworkAddress { get; set; } = string.Empty;

    [Required(ErrorMessage = "CIDR notation is required")]
    [Range(0, 32, ErrorMessage = "CIDR must be between 0 and 32")]
    [Display(Name = "CIDR Notation")]
    public int Cidr { get; set; }

    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Parent Subnet")]
    public int? ParentSubnetId { get; set; }

    [Display(Name = "Tags")]
    [StringLength(255, ErrorMessage = "Tags cannot be longer than 255 characters")]
    public string? Tags { get; set; }

    // Navigation properties (not mapped to database)
    public List<SubnetViewModel> ParentSubnetOptions { get; set; } = [];

    // Helper property for display
    public string CalculatedSubnetMask { get; set; } = string.Empty;
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
    public long TotalIpAddresses { get; set; }
    public long UsableIpAddresses { get; set; }
    public int? ParentSubnetId { get; set; }
    public bool IsFullyAllocated { get; set; }
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
    public int? ParentSubnetId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }

    // Calculated properties
    public string SubnetMask { get; set; } = string.Empty;
    public string BroadcastAddress { get; set; } = string.Empty;
    public long TotalIpAddresses { get; set; }
    public long UsableIpAddresses { get; set; }

    // Child subnets
    public List<SubnetViewModel> ChildSubnets { get; set; } = [];

    // Host IP assignments
    public List<HostIpViewModel> HostIpAssignments { get; set; } = [];
    public bool IsFullyAllocated { get; set; }
    
    // Helper properties for UI logic
    public int HostIpCount => HostIpAssignments.Count;
    public bool CanAddHostIp => ChildSubnets.Count == 0 && !IsFullyAllocated;
    public bool CanAddChildSubnet => HostIpAssignments.Count == 0 && !IsFullyAllocated;

    // Unallocated ranges
    public List<IPRange> UnallocatedRanges { get; set; } = [];

    // Parent subnet info (for display)
    public string? ParentSubnetName { get; set; }
    public string? ParentNetworkAddress { get; set; }
}
