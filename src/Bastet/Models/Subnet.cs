using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bastet.Models;

public class Subnet : BaseEntity
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

    [MaxLength(500)]
    public string? AzureResourceId { get; set; }

    public int? ParentSubnetId { get; set; }

    [ForeignKey(nameof(ParentSubnetId))]
    public Subnet? ParentSubnet { get; set; }

    public ICollection<Subnet> ChildSubnets { get; set; } = [];

    public ICollection<HostIpAssignment> HostIpAssignments { get; set; } = [];

    public bool IsFullyAllocated { get; set; } = false;

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
