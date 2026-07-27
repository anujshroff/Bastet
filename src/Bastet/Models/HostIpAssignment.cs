using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bastet.Models;

/// <summary>
/// Represents a host IP assignment within a subnet
/// </summary>
public class HostIpAssignment : BaseEntity
{
    [Key]
    [MaxLength(15)] // IPv4 addresses are max 15 characters
    public string IP { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Name { get; set; }

    // Foreign key relationship
    public int SubnetId { get; set; }

    [ForeignKey(nameof(SubnetId))]
    public Subnet Subnet { get; set; } = null!;

    // Concurrency control
    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
