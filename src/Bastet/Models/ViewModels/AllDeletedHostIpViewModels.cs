namespace Bastet.Models.ViewModels;

/// <summary>
/// View model for listing all deleted host IP assignments across all subnets
/// </summary>
public class AllDeletedHostIpsViewModel
{
    public List<AllDeletedHostIpItemViewModel> DeletedHostIps { get; set; } = [];
    public int TotalCount { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

/// <summary>
/// View model for a single deleted host IP item in the all deleted host IPs list
/// </summary>
public class AllDeletedHostIpItemViewModel
{
    // Original IP information
    public int Id { get; set; }
    public string OriginalIP { get; set; } = string.Empty;
    public string? Name { get; set; }

    // Original subnet information
    public int OriginalSubnetId { get; set; }

    /// <summary>
    /// The original subnet's name, or "Unknown" when neither a live nor an archived subnet carries
    /// the recorded id.
    /// </summary>
    /// <remarks>
    /// No address range accompanies it. The prefix the subnet had when this address was archived is
    /// not stored, and the current one is not a truthful substitute under a column headed "Original
    /// Subnet" - see the comment in AllDeletedHostIps.cshtml.
    /// </remarks>
    public string SubnetName { get; set; } = string.Empty;

    // Metadata
    public DateTime DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
}
