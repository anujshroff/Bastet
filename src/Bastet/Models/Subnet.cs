using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bastet.Models;

/// <summary>
/// Represents a subnet in the BASTET system
/// </summary>
public class Subnet : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(15)] // IPv4 addresses are max 15 characters
    public string NetworkAddress { get; set; } = string.Empty;

    [Required]
    [Range(0, 32)] // IPv4 supports up to /32
    public int Cidr { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(255)]
    public string? Tags { get; set; }

    [MaxLength(500)]
    public string? AzureResourceId { get; set; }

    // Parent-Child Relationship
    public int? ParentSubnetId { get; set; }

    [ForeignKey(nameof(ParentSubnetId))]
    public Subnet? ParentSubnet { get; set; }

    public ICollection<Subnet> ChildSubnets { get; set; } = [];

    // Host IP Assignment Relationship
    public ICollection<HostIpAssignment> HostIpAssignments { get; set; } = [];

    // Flag to indicate if subnet is fully allocated (no IPs available)
    public bool IsFullyAllocated { get; set; } = false;

    // Concurrency control
    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
