using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bastet.Models;

public class HostIpAssignment : BaseEntity
{
    [Key]
    [MaxLength(15)]
    public string IP { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Name { get; set; }

    public int SubnetId { get; set; }

    [ForeignKey(nameof(SubnetId))]
    public Subnet Subnet { get; set; } = null!;

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
