namespace Bastet.Models.ViewModels
{
    /// <summary>
    /// Represents an Azure subscription
    /// </summary>
    public class AzureSubscriptionViewModel
    {
        /// <summary>
        /// The Azure subscription ID
        /// </summary>
        public string SubscriptionId { get; set; } = string.Empty;

        /// <summary>
        /// The display name of the subscription
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents an Azure Virtual Network (VNet)
    /// </summary>
    public class AzureVNetViewModel
    {
        /// <summary>
        /// The resource ID of the VNet
        /// </summary>
        public string ResourceId { get; set; } = string.Empty;

        /// <summary>
        /// The name of the VNet
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The address prefixes (CIDR notation) of the VNet
        /// </summary>
        public List<string> AddressPrefixes { get; set; } = [];
    }

    /// <summary>
    /// Represents an Azure Subnet
    /// </summary>
    public class AzureSubnetViewModel
    {
        /// <summary>
        /// The Azure resource ID of the subnet
        /// </summary>
        public string ResourceId { get; set; } = string.Empty;

        /// <summary>
        /// The name of the subnet
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The address prefix (CIDR notation) to use for this subnet
        /// For subnets with both IPv4 and IPv6, this will contain the IPv4 prefix
        /// </summary>
        public string AddressPrefix { get; set; } = string.Empty;

        /// <summary>
        /// Indicates whether this subnet had multiple address schemes (IPv4 and IPv6)
        /// </summary>
        public bool HasMultipleAddressSchemes { get; set; }

        /// <summary>
        /// Indicates whether this subnet fully encompasses one of the VNet's address prefixes
        /// </summary>
        public bool FullyEncompassesVNetPrefix { get; set; }
    }

    /// <summary>
    /// Main view model for the Azure Import process
    /// </summary>
    public class AzureImportViewModel
    {
        /// <summary>
        /// The ID of the Bastet subnet to import into
        /// </summary>
        public int SubnetId { get; set; }

        /// <summary>
        /// The name of the Bastet subnet
        /// </summary>
        public string SubnetName { get; set; } = string.Empty;

        /// <summary>
        /// The network address of the Bastet subnet
        /// </summary>
        public string NetworkAddress { get; set; } = string.Empty;

        /// <summary>
        /// The CIDR of the Bastet subnet
        /// </summary>
        public int Cidr { get; set; }
    }
}
