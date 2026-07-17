namespace Bastet.Models.ViewModels
{
    /// <summary>
    /// The result of reading every VNet in a subscription, carrying whether the read succeeded.
    /// </summary>
    /// <remarks>
    /// Reconcile inverts the meaning of an empty result: for import, no VNets means "nothing to do";
    /// for reconcile, it means "everything in Bastet is stale, delete it all". That makes the
    /// difference between a failed call and an empty subscription safety-critical, so it is modelled
    /// explicitly rather than inferred from an empty list.
    /// </remarks>
    public class AzureVNetInventory
    {
        /// <summary>
        /// True only when Azure was successfully queried. When false, <see cref="VNets"/> says
        /// nothing about what exists in Azure and must not be used to conclude anything is gone.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Why the read failed, for display. Null on success.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Every VNet in the subscription with its IPv4 prefixes and IPv4 subnets.
        /// </summary>
        public List<BulkAzureVNetViewModel> VNets { get; set; } = [];
    }

    /// <summary>
    /// A Bastet subnet that was imported from Azure, plus the blast radius of deleting it.
    /// Keeps the reconciler free of EF, mirroring <see cref="ExistingSubnetSnapshot"/>.
    /// </summary>
    public class AzureLinkedSubnetSnapshot
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string NetworkAddress { get; set; } = string.Empty;
        public int Cidr { get; set; }

        /// <summary>
        /// The ARM resource ID recorded at import. A VNet ID on import targets, a subnet ID on
        /// imported children - tell them apart by the presence of a "/subnets/" segment.
        /// </summary>
        public string AzureResourceId { get; set; } = string.Empty;

        /// <summary>
        /// Set by import when an Azure subnet covered the target's whole prefix, but also settable
        /// by hand, so it is never on its own proof that a row came from Azure.
        /// </summary>
        public bool IsFullyAllocated { get; set; }

        /// <summary>Descendant subnets that deleting this subnet would archive with it.</summary>
        public int DescendantCount { get; set; }

        /// <summary>Host IP assignments on this subnet and all of its descendants.</summary>
        public int HostIpCount { get; set; }

        /// <summary>
        /// IDs of every subnet in this subnet's subtree - the rows the counts above include.
        /// </summary>
        public IReadOnlyList<int> DescendantSubnetIds { get; set; } = [];
    }

    /// <summary>
    /// Why a Bastet subnet no longer lines up with Azure.
    /// </summary>
    public enum AzureReconcileStatus
    {
        /// <summary>The VNet this subnet was imported from no longer exists.</summary>
        VNetDeleted,

        /// <summary>The VNet still exists but no longer has this address prefix.</summary>
        VNetPrefixRemoved,

        /// <summary>The Azure subnet this was imported from no longer exists.</summary>
        SubnetDeleted,

        /// <summary>The Azure subnet still exists but its address prefix changed.</summary>
        SubnetPrefixChanged,

        /// <summary>
        /// The subnet is marked fully allocated, and its VNet and prefix both still exist, but no
        /// Azure subnet covers the prefix any more - so whatever justified the flag is gone.
        /// Reported for review only: nothing here should be deleted, and the flag is never cleared
        /// automatically because it may have been set by hand.
        /// </summary>
        FullyAllocatingSubnetDeleted
    }

    /// <summary>
    /// One Bastet subnet that has drifted from Azure.
    /// </summary>
    public class AzureReconcileItem
    {
        public int SubnetId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string NetworkAddress { get; set; } = string.Empty;
        public int Cidr { get; set; }
        public string AzureResourceId { get; set; } = string.Empty;
        public AzureReconcileStatus Status { get; set; }

        /// <summary>Human-readable explanation shown next to the row.</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>True when the recorded resource ID is a VNet rather than an Azure subnet.</summary>
        public bool IsVNetLevel { get; set; }

        /// <summary>Descendants that would be archived along with this subnet.</summary>
        public int DescendantCount { get; set; }

        /// <summary>Host IPs that would be archived along with this subnet and its descendants.</summary>
        public int HostIpCount { get; set; }

        /// <summary>
        /// IDs of every subnet in this subnet's subtree - the rows the counts above include. Lets
        /// the client avoid double-counting when an item and its ancestor are both selected.
        /// </summary>
        public IReadOnlyList<int> DescendantSubnetIds { get; set; } = [];

        /// <summary>The status name, so clients don't depend on the enum's ordinal.</summary>
        public string StatusName => Status.ToString();
    }

    /// <summary>
    /// The outcome of reconciling one subscription against Bastet.
    /// </summary>
    public class AzureReconcilePlanViewModel
    {
        public string SubscriptionId { get; set; } = string.Empty;
        public string? SubscriptionName { get; set; }

        /// <summary>
        /// False when Azure could not be read. <see cref="Items"/> is empty in that case: a scan that
        /// failed must never offer anything for deletion.
        /// </summary>
        public bool ScanSucceeded { get; set; }

        /// <summary>Subnets that are gone from Azure and may be deleted.</summary>
        public List<AzureReconcileItem> Items { get; set; } = [];

        /// <summary>
        /// Drift that is reported but not actionable here. Never gates <see cref="CanCommit"/>.
        /// </summary>
        public List<AzureReconcileItem> ReviewItems { get; set; } = [];

        public List<string> GlobalErrors { get; set; } = [];

        /// <summary>Things the user should weigh before deleting, without blocking them.</summary>
        public List<string> Warnings { get; set; } = [];

        public bool CanCommit => ScanSucceeded && GlobalErrors.Count == 0 && Items.Count > 0;
    }

    /// <summary>
    /// Landing page model for the reconcile view.
    /// </summary>
    public class AzureReconcileInitialViewModel
    {
        public bool IsFeatureEnabled { get; set; }
    }

    /// <summary>
    /// The commit request: which subnets to delete, and the typed confirmation.
    /// </summary>
    public class AzureReconcileDeleteDto
    {
        public string SubscriptionId { get; set; } = string.Empty;

        /// <summary>
        /// Bastet subnet IDs to delete. Deliberately IDs rather than a plan: the server re-scans and
        /// only deletes what is still stale, so a stale client view cannot delete the wrong rows.
        /// </summary>
        public List<int> SubnetIds { get; set; } = [];

        /// <summary>Must be "approved", matching the single-subnet delete flow.</summary>
        public string Confirmation { get; set; } = string.Empty;
    }
}
