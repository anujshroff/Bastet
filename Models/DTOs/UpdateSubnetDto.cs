using System.ComponentModel.DataAnnotations;

namespace Bastet.Models.DTOs;

/// <summary>
/// DTO for updating an existing subnet
/// </summary>
public class UpdateSubnetDto
{
    [Required(ErrorMessage = "Name is required")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 50 characters")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    public string? Description { get; set; }

    [StringLength(255, ErrorMessage = "Tags cannot exceed 255 characters")]
    public string? Tags { get; set; }
}
