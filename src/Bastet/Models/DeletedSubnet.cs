using System.ComponentModel.DataAnnotations;

namespace Bastet.Models;

public class DeletedSubnet
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(15)]
    public string NetworkAddress { get; set; } = string.Empty;

    [Required]
    [Range(0, 32)]
    public int Cidr { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(255)]
    public string? Tags { get; set; }

    public int OriginalId { get; set; }
    public int? OriginalParentId { get; set; }

    public DateTime DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
}
