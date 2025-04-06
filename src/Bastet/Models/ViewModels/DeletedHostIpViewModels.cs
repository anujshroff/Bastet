namespace Bastet.Models.ViewModels;

/// <summary>
/// View model for displaying a list of deleted host IP assignments
/// </summary>
public class DeletedHostIpListViewModel
{
    public List<DeletedHostIpViewModel> DeletedHostIps { get; set; } = [];
    public int TotalCount { get; set; }
    public int SubnetId { get; set; }
    public string SubnetName { get; set; } = string.Empty;
    public string NetworkAddress { get; set; } = string.Empty;
    public int Cidr { get; set; }
}

/// <summary>
/// View model for a deleted host IP assignment
/// </summary>
public class DeletedHostIpViewModel
{
    public int Id { get; set; }
    public string OriginalIP { get; set; } = string.Empty;
    public string? Name { get; set; }
    public int OriginalSubnetId { get; set; }
    public DateTime DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
}
