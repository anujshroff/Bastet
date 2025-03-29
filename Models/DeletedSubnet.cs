using System.ComponentModel.DataAnnotations;

namespace Bastet.Models;

/// <summary>
/// Represents a deleted subnet in the BASTET system
/// </summary>
public class DeletedSubnet
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(15)] // IPv4 addresses are max 15 characters
    public string NetworkAddress { get; set; } = string.Empty;

    [Required]
    [Range(0, 32)] // IPv4 supports up to /32
    public int Cidr { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(255)]
    public string? Tags { get; set; }

    // Original relationships
    public int OriginalId { get; set; }
    public int? OriginalParentId { get; set; }

    // Deletion metadata
    public DateTime DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    // Original audit data
    public DateTime CreatedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
}
