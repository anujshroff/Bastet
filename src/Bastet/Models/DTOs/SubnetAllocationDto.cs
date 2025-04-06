using System.ComponentModel.DataAnnotations;

namespace Bastet.Models.DTOs;

/// <summary>
/// DTO for updating a subnet's allocation status
/// </summary>
public class SubnetAllocationDto
{
    [Required]
    public int SubnetId { get; set; }

    [Required]
    public bool IsFullyAllocated { get; set; }
}
