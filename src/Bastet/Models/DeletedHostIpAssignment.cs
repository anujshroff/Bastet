using System.ComponentModel.DataAnnotations;

namespace Bastet.Models;

public class DeletedHostIpAssignment
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(15)]
    public string OriginalIP { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Name { get; set; }

    public int OriginalSubnetId { get; set; }

    public DateTime DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
}
