using System.ComponentModel.DataAnnotations;

namespace Bastet.Models.ViewModels;

/// <summary>
/// View model for editing subnet metadata (non-network properties)
/// </summary>
public class EditSubnetViewModel
{
    /// <summary>
    /// The subnet ID (hidden field)
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The network address (display only)
    /// </summary>
    [Display(Name = "Network Address")]
    public string NetworkAddress { get; set; } = string.Empty;

    /// <summary>
    /// The CIDR notation (display only)
    /// </summary>
    [Display(Name = "CIDR")]
    public int Cidr { get; set; }

    // Editable properties

    [Required(ErrorMessage = "Name is required")]
    [StringLength(50, ErrorMessage = "Name cannot be longer than 50 characters")]
    [Display(Name = "Subnet Name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Description")]
    [StringLength(500, ErrorMessage = "Description cannot be longer than 500 characters")]
    public string? Description { get; set; }

    [Display(Name = "Tags")]
    [StringLength(255, ErrorMessage = "Tags cannot be longer than 255 characters")]
    public string? Tags { get; set; }

    // Additional display-only properties
    [Display(Name = "Subnet Mask")]
    public string SubnetMask { get; set; } = string.Empty;

    [Display(Name = "Parent Subnet")]
    public string? ParentSubnetInfo { get; set; }

    [Display(Name = "Created")]
    public DateTime CreatedAt { get; set; }

    [Display(Name = "Last Modified")]
    public DateTime? LastModifiedAt { get; set; }
}
