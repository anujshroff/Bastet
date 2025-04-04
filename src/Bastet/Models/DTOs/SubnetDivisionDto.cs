using System.ComponentModel.DataAnnotations;

namespace Bastet.Models.DTOs;

/// <summary>
/// Data transfer object for subnet division operations
/// </summary>
public class SubnetDivisionDto
{
    /// <summary>
    /// The target CIDR for the child subnets
    /// </summary>
    [Required(ErrorMessage = "Target CIDR is required")]
    [Range(1, 32, ErrorMessage = "Target CIDR must be between 1 and 32")]
    public int TargetCidr { get; set; }

    /// <summary>
    /// Optional prefix for naming the subnets
    /// </summary>
    [StringLength(40, ErrorMessage = "Name prefix cannot exceed 40 characters")]
    public string? NamePrefix { get; set; }

    /// <summary>
    /// Optional description template for the subnets
    /// </summary>
    [StringLength(450, ErrorMessage = "Description template cannot exceed 450 characters")]
    public string? DescriptionTemplate { get; set; }

    /// <summary>
    /// Optional - specific networks to create (if null, create all possible subnets or up to Count)
    /// </summary>
    public List<string>? SpecificNetworks { get; set; }

    /// <summary>
    /// Optional - number of subnets to create (starting from the first possible)
    /// If not specified and SpecificNetworks is null, all possible subnets will be created
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "Count must be at least 1")]
    public int? Count { get; set; }

    /// <summary>
    /// Optional - tags to assign to the created subnets
    /// </summary>
    [StringLength(255, ErrorMessage = "Tags cannot exceed 255 characters")]
    public string? Tags { get; set; }
}
