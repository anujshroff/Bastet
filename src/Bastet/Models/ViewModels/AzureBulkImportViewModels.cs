namespace Bastet.Models.ViewModels
{

    public enum BulkImportAvailability
    {

        Available,

        WillUpdateExisting,

        AlreadyImported,

        Blocked
    }

    public class BulkAzureSubnetViewModel
    {

        public string ResourceId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string AddressPrefix { get; set; } = string.Empty;

        public List<string> Ipv4AddressPrefixes { get; set; } = [];

        public BulkImportAvailability Status { get; set; } = BulkImportAvailability.Available;

        public string StatusName => Status.ToString();

        public string? Reason { get; set; }

        public bool IsSelectable { get; set; } = true;
    }

    public class BulkAzurePrefixViewModel
    {

        public string AddressPrefix { get; set; } = string.Empty;

        public BulkImportAvailability Status { get; set; } = BulkImportAvailability.Available;

        public string StatusName => Status.ToString();

        public string? Reason { get; set; }

        public bool IsSelectable { get; set; } = true;
    }

    public class BulkAzureVNetViewModel
    {

        public string ResourceId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public List<string> Ipv4AddressPrefixes { get; set; } = [];

        public List<BulkAzurePrefixViewModel> Prefixes { get; set; } = [];

        public List<BulkAzureSubnetViewModel> Subnets { get; set; } = [];
    }

    public class BulkImportInitialViewModel
    {

        public bool IsFeatureEnabled { get; set; }
    }

    public class BulkImportSelectedSubnetDto
    {
        public string Name { get; set; } = string.Empty;

        public string AddressPrefix { get; set; } = string.Empty;

        public string AzureResourceId { get; set; } = string.Empty;
    }

    public class BulkImportSelectedVNetPrefixDto
    {

        public string VNetName { get; set; } = string.Empty;

        public string VNetResourceId { get; set; } = string.Empty;

        public string AddressPrefix { get; set; } = string.Empty;

        public List<BulkImportSelectedSubnetDto> Subnets { get; set; } = [];

        public BulkImportExpectedTargetDto? Expected { get; set; }
    }

    public class BulkImportExpectedTargetDto
    {

        public string? TargetType { get; set; }

        public int? ExistingTargetSubnetId { get; set; }

        public int? AutoCreateParentSubnetId { get; set; }

        public bool WillRename { get; set; }

        public string? NewName { get; set; }

        public bool WillMarkFullyAllocated { get; set; }

        public List<string>? ChildNames { get; set; }
    }

    public class BulkImportSelectionDto
    {
        public string SubscriptionId { get; set; } = string.Empty;

        public string? SubscriptionName { get; set; }

        public List<BulkImportSelectedVNetPrefixDto> VNetPrefixes { get; set; } = [];

        public bool RenameMatchedBastetSubnets { get; set; }
    }

    public class ExistingSubnetSnapshot
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string NetworkAddress { get; set; } = string.Empty;
        public int Cidr { get; set; }
        public bool HasChildSubnets { get; set; }
        public bool HasHostIpAssignments { get; set; }
        public bool IsFullyAllocated { get; set; }

        public string? AzureResourceId { get; set; }
    }

    public enum BulkImportTargetType
    {

        ExactMatch,

        AutoCreateChild,

        AutoCreateTopLevel
    }

    public class BulkImportPlannedChildSubnet
    {

        public string OriginalAzureName { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string NetworkAddress { get; set; } = string.Empty;

        public int Cidr { get; set; }

        public string AzureResourceId { get; set; } = string.Empty;
    }

    public class BulkImportPlanItem
    {
        public string VNetName { get; set; } = string.Empty;
        public string VNetResourceId { get; set; } = string.Empty;

        public string VNetPrefix { get; set; } = string.Empty;

        public string PrefixNetworkAddress { get; set; } = string.Empty;
        public int PrefixCidr { get; set; }

        public BulkImportTargetType TargetType { get; set; }

        public string TargetTypeName => TargetType.ToString();

        public int? ExistingTargetSubnetId { get; set; }
        public string? ExistingTargetSubnetName { get; set; }

        public int? AutoCreateParentSubnetId { get; set; }
        public string? AutoCreateParentSubnetName { get; set; }

        public string? AutoCreateTargetName { get; set; }

        public bool WillRename { get; set; }

        public string? NewName { get; set; }

        public bool WillMarkFullyAllocated { get; set; }

        public string? FullyAllocatingAzureSubnetName { get; set; }

        public List<BulkImportPlannedChildSubnet> ChildSubnets { get; set; } = [];

        public List<string> Errors { get; set; } = [];

        public List<string> Warnings { get; set; } = [];
    }

    public class BulkImportPlanViewModel
    {
        public string SubscriptionId { get; set; } = string.Empty;
        public string? SubscriptionName { get; set; }
        public bool RenameMatchedBastetSubnets { get; set; }

        public List<BulkImportPlanItem> Items { get; set; } = [];

        public List<string> GlobalErrors { get; set; } = [];

        public bool CanCommit => GlobalErrors.Count == 0 && Items.All(i => i.Errors.Count == 0);
    }
}
