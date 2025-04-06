namespace Bastet.Models.ViewModels;

/// <summary>
/// View model for subnet deletion confirmation
/// </summary>
public class DeleteSubnetViewModel
{
    /// <summary>
    /// Subnet ID
    /// </summary>
    public int Id { get; set; }

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
    /// Number of child subnets that will be deleted
    /// </summary>
    public int ChildSubnetCount { get; set; }
    
    /// <summary>
    /// Number of host IP assignments that will be deleted
    /// </summary>
    public int HostIpCount { get; set; }
    
    /// <summary>
    /// Indicates if the subnet is fully allocated
    /// </summary>
    public bool IsFullyAllocated { get; set; }

    /// <summary>
    /// Confirmation text (should be "approved")
    /// </summary>
    public string? Confirmation { get; set; }
}
