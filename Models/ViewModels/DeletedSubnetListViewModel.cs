namespace Bastet.Models.ViewModels;

/// <summary>
/// View model for displaying a list of deleted subnets
/// </summary>
public class DeletedSubnetListViewModel
{
    /// <summary>
    /// Collection of deleted subnet view models
    /// </summary>
    public IEnumerable<DeletedSubnetsViewModel> DeletedSubnets { get; set; } = [];

    /// <summary>
    /// Total count of deleted subnets
    /// </summary>
    public int TotalCount { get; set; }
}
