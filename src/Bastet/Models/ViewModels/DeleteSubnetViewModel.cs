namespace Bastet.Models.ViewModels;

public class DeleteSubnetViewModel
{

    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string NetworkAddress { get; set; } = string.Empty;

    public int Cidr { get; set; }

    public string? Description { get; set; }

    public int ChildSubnetCount { get; set; }

    public int HostIpCount { get; set; }

}
