using System.ComponentModel.DataAnnotations;

namespace Bastet.Models.DTOs;

/// <summary>
/// DTO for creating a new subnet
/// </summary>
public class CreateSubnetDto
{
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
}
