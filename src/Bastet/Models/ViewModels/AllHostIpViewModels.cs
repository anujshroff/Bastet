namespace Bastet.Models.ViewModels;

/// <summary>
/// View model for listing all host IP assignments across all subnets
/// </summary>
public class AllHostIpsViewModel
{
    public List<AllHostIpItemViewModel> HostIps { get; set; } = [];
    public int TotalCount { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

/// <summary>
/// View model for a single host IP item in the all host IPs list
/// </summary>
public class AllHostIpItemViewModel
{
    // IP information
    public string IP { get; set; } = string.Empty;
    public string? Name { get; set; }
    
    // Subnet information
    public int SubnetId { get; set; }
    public string SubnetName { get; set; } = string.Empty;
    public string NetworkAddress { get; set; } = string.Empty;
    public int Cidr { get; set; }
    
    // Metadata
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
}
