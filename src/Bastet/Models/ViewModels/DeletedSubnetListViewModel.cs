namespace Bastet.Models.ViewModels;

public class DeletedSubnetListViewModel
{

    public IEnumerable<DeletedSubnetsViewModel> DeletedSubnets { get; set; } = [];

    public int TotalCount { get; set; }
}
