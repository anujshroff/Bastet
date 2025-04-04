using System.ComponentModel.DataAnnotations;

namespace Bastet.Models.DTOs;

/// <summary>
/// Base DTO for subnet information
/// </summary>
public class SubnetDto
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Name is required")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 50 characters")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Network address is required")]
    [RegularExpression(@"^(\d{1,3}\.){3}\d{1,3}$",
        ErrorMessage = "Invalid IPv4 address format")]
    public string NetworkAddress { get; set; } = string.Empty;

    [Required(ErrorMessage = "CIDR is required")]
    [Range(0, 32, ErrorMessage = "CIDR must be between 0 and 32")]
    public int Cidr { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    public string? Description { get; set; }

    [StringLength(255, ErrorMessage = "Tags cannot exceed 255 characters")]
    public string? Tags { get; set; }

    public int? ParentSubnetId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }

    // Calculated properties
    public string? SubnetMask { get; set; }
    public string? BroadcastAddress { get; set; }
    public long TotalIpAddresses { get; set; }
    public long UsableIpAddresses { get; set; }
}

/// <summary>
/// Detailed subnet information including child subnets and unallocated ranges
/// </summary>
public class SubnetDetailDto : SubnetDto
{
    public List<SubnetDto> ChildSubnets { get; set; } = [];
    public List<IPRange> UnallocatedRanges { get; set; } = [];
}
