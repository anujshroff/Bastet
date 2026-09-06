namespace Bastet.Models.ViewModels;

public class DeletedHostIpListViewModel
{
    public List<DeletedHostIpViewModel> DeletedHostIps { get; set; } = [];
    public int TotalCount { get; set; }
    public int SubnetId { get; set; }
    public string SubnetName { get; set; } = string.Empty;
    public string NetworkAddress { get; set; } = string.Empty;
    public int Cidr { get; set; }
}

public class DeletedHostIpViewModel
{
    public string OriginalIP { get; set; } = string.Empty;
    public string? Name { get; set; }
    public DateTime DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
