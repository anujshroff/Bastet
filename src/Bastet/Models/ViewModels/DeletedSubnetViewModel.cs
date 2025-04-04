namespace Bastet.Models.ViewModels;

/// <summary>
/// View model for displaying deleted subnets
/// </summary>
public class DeletedSubnetsViewModel
{
    /// <summary>
    /// Original subnet ID
    /// </summary>
    public int OriginalId { get; set; }

    /// <summary>
    /// Subnet name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Network address (IPv4)
    /// </summary>
    public string NetworkAddress { get; set; } = string.Empty;

    /// <summary>
    /// CIDR notation
    /// </summary>
    public int Cidr { get; set; }

    /// <summary>
    /// Optional description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Original parent subnet ID (if any)
    /// </summary>
    public int? OriginalParentId { get; set; }

    /// <summary>
    /// When the subnet was deleted
    /// </summary>
    public DateTime DeletedAt { get; set; }

    /// <summary>
    /// Who deleted the subnet
    /// </summary>
    public string? DeletedBy { get; set; }

    /// <summary>
    /// When the subnet was originally created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the subnet was last modified before deletion
    /// </summary>
    public DateTime? LastModifiedAt { get; set; }

    /// <summary>
    /// Who originally created the subnet
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Who last modified the subnet before deletion
    /// </summary>
    public string? ModifiedBy { get; set; }
}
