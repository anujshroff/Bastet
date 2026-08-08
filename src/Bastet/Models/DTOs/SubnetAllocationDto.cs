using System.ComponentModel.DataAnnotations;

namespace Bastet.Models.DTOs;

public class SubnetAllocationDto
{
    [Required]
    public int SubnetId { get; set; }

    [Required]
    public bool IsFullyAllocated { get; set; }
}
