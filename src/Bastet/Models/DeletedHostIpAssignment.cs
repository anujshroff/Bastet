using System.ComponentModel.DataAnnotations;

namespace Bastet.Models;

/// <summary>
/// Represents a deleted host IP assignment in the BASTET system
/// </summary>
public class DeletedHostIpAssignment
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(15)] // IPv4 addresses are max 15 characters
    public string OriginalIP { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Name { get; set; }

    // Original relationship
    public int OriginalSubnetId { get; set; }

    // Deletion metadata
    public DateTime DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    // Original audit data
    public DateTime CreatedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
}
