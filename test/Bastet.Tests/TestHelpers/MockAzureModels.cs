namespace Bastet.Tests.TestHelpers;

public class AzureVNetViewModel
{
    public string ResourceId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<string> AddressPrefixes { get; set; } = [];
}

public class AzureSubnetViewModel
{
    public string ResourceId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string AddressPrefix { get; set; } = string.Empty;
}
