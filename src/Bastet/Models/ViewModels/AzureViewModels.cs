namespace Bastet.Models.ViewModels
{

    public class AzureSubscriptionViewModel
    {

        public string SubscriptionId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;
    }

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

        public bool HasMultipleAddressSchemes { get; set; }

        public bool FullyEncompassesVNetPrefix { get; set; }

        public BulkImportAvailability Status { get; set; } = BulkImportAvailability.Available;

        public string StatusName => Status.ToString();

        public string? Reason { get; set; }

        public bool IsSelectable { get; set; } = true;
    }

    public class AzureImportViewModel
    {

        public int SubnetId { get; set; }

        public string SubnetName { get; set; } = string.Empty;

        public string NetworkAddress { get; set; } = string.Empty;

        public int Cidr { get; set; }
    }
}
