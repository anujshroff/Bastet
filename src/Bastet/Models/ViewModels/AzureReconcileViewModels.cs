namespace Bastet.Models.ViewModels
{

    public class AzureVNetInventory
    {

        public bool Success { get; set; }

        public string? ErrorMessage { get; set; }

        public List<BulkAzureVNetViewModel> VNets { get; set; } = [];
    }

    public class AzureLinkedSubnetSnapshot
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string NetworkAddress { get; set; } = string.Empty;
        public int Cidr { get; set; }

        public string AzureResourceId { get; set; } = string.Empty;

        public bool IsFullyAllocated { get; set; }

        public int DescendantCount { get; set; }

        public int HostIpCount { get; set; }

        public IReadOnlyList<int> DescendantSubnetIds { get; set; } = [];
    }

    public enum AzureResourceConfirmation
    {

        Live,

        Deleted,

        NotVisible,

        Unknown
    }

    public enum AzureReconcileStatus
    {

        VNetDeleted,

        VNetPrefixRemoved,

        SubnetDeleted,

        SubnetPrefixChanged,

        FullyAllocatingSubnetDeleted,

        UnrecognisedResourceId,

        RangeStillAllocatedInAzure,

        VNetPrefixStillCovered,

        AzureRangeNotImported
    }

    public class AzureReconcileItem
    {
        public int SubnetId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string NetworkAddress { get; set; } = string.Empty;
        public int Cidr { get; set; }
        public string AzureResourceId { get; set; } = string.Empty;
        public AzureReconcileStatus Status { get; set; }

        public string Reason { get; set; } = string.Empty;

        public bool IsVNetLevel { get; set; }

        public int DescendantCount { get; set; }

        public int HostIpCount { get; set; }

        public IReadOnlyList<int> DescendantSubnetIds { get; set; } = [];

        public string SuggestedAzureResourceId { get; set; } = string.Empty;

        public string SuggestedAzureSubnetName { get; set; } = string.Empty;

        public string StatusName => Status.ToString();
    }

    public class AzureReconcilePlanViewModel
    {
        public string SubscriptionId { get; set; } = string.Empty;
        public string? SubscriptionName { get; set; }

        public bool ScanSucceeded { get; set; }

        public List<AzureReconcileItem> Items { get; set; } = [];

        public List<AzureReconcileItem> ReviewItems { get; set; } = [];

        public List<string> GlobalErrors { get; set; } = [];

        public List<string> Warnings { get; set; } = [];

        public bool CanCommit => ScanSucceeded && GlobalErrors.Count == 0 && Items.Count > 0;
    }

    public class AzureReconcileInitialViewModel
    {
        public bool IsFeatureEnabled { get; set; }
    }

    public class AzureReconcileDeleteDto
    {
        public string SubscriptionId { get; set; } = string.Empty;

        public List<int> SubnetIds { get; set; } = [];

        public string Confirmation { get; set; } = string.Empty;

        public List<AzureReconcileApprovedVerdict> Statuses { get; set; } = [];
    }

    public class AzureReconcileApprovedVerdict
    {
        public int SubnetId { get; set; }

        public string StatusName { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;
    }

    public class AzureRelinkDto
    {
        public string SubscriptionId { get; set; } = string.Empty;

        public int SubnetId { get; set; }
    }
}
