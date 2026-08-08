namespace Bastet.Models.ViewModels;

public class AllDeletedHostIpsViewModel
{
    public List<AllDeletedHostIpItemViewModel> DeletedHostIps { get; set; } = [];
    public int TotalCount { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

public class AllDeletedHostIpItemViewModel
{

    public int Id { get; set; }
    public string OriginalIP { get; set; } = string.Empty;
    public string? Name { get; set; }

    public int OriginalSubnetId { get; set; }

    public string SubnetName { get; set; } = string.Empty;

    public DateTime DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
}
