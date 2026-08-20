namespace Bastet.Models.ViewModels;

public class DeletedSubnetsViewModel
{

    public string Name { get; set; } = string.Empty;

    public string NetworkAddress { get; set; } = string.Empty;

    public int Cidr { get; set; }

    public string? Description { get; set; }

    public DateTime DeletedAt { get; set; }

    public string? DeletedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastModifiedAt { get; set; }

    public string? CreatedBy { get; set; }

    public string? ModifiedBy { get; set; }
}
