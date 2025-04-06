using System.ComponentModel.DataAnnotations;

namespace Bastet.Models.DTOs;

/// <summary>
/// DTO for creating a new host IP assignment
/// </summary>
public class CreateHostIpDto
{
    [Required(ErrorMessage = "IP address is required")]
    [RegularExpression(@"^(\d{1,3}\.){3}\d{1,3}$", 
        ErrorMessage = "Invalid IPv4 address format")]
    public string IP { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
    public string? Name { get; set; }

    [Required(ErrorMessage = "Subnet ID is required")]
    public int SubnetId { get; set; }
}
