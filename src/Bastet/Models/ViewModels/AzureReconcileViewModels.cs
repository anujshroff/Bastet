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
    /// What an individual check of one ARM resource established.
    /// </summary>
    /// <remarks>
    /// A subscription-scoped list is RBAC-filtered: a principal that cannot see a resource group
    /// gets HTTP 200 with those resources simply missing, not a 403. So absence from an inventory
    /// cannot distinguish "deleted" from "not visible to this credential", and only a direct read of
    /// the resource itself can. 404 and 403 are distinct on that read, which is what makes this
    /// possible.
    /// </remarks>
    public enum AzureResourceConfirmation
    {
        /// <summary>The resource still exists and is readable. Nothing to delete.</summary>
        Live,

        /// <summary>Azure returned 404. The resource is genuinely gone.</summary>
        Deleted,

        /// <summary>
        /// Azure returned 403. The resource may well exist - this credential just cannot see it.
        /// Never a reason to archive anything.
        /// </summary>
        NotVisible,

        /// <summary>
        /// The check could not be completed - throttling, a transport error, an unparseable ID.
        /// Treated exactly like <see cref="NotVisible"/>: an unanswered question is not a deletion.
        /// </summary>
        Unknown
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
        FullyAllocatingSubnetDeleted,

        /// <summary>
        /// The stored resource ID names neither a VNet nor a subnet, so nothing can be established
        /// about it. Reported for review only: the Azure SDK builds its request from the resource
        /// group and the last path segment alone, so reading such an ID asks about a different
        /// resource entirely and its 404 would otherwise read as a confirmed deletion.
        /// </summary>
        UnrecognisedResourceId,

        /// <summary>
        /// The recorded Azure resource is gone or no longer carries this prefix, but the range
        /// itself is still assigned in the same VNet under a different Azure resource ID - which is
        /// what a subnet rename looks like, Azure having no rename operation.
        /// Reported for review only and never deletable: archiving the row would make BASTET
        /// advertise an allocated range as free space. Correct it by re-linking the subnet to the
        /// Azure subnet that now holds the range.
        /// </summary>
        RangeStillAllocatedInAzure,

        /// <summary>
        /// Azure has assigned a range inside an imported VNet that no BASTET subnet records, so
        /// BASTET is reporting an allocated range as free space.
        /// Reported for review only and never deletable - it names no BASTET subnet, because the
        /// absence of one IS the finding. This is the only inbound verdict: every other status
        /// starts from a BASTET row and asks what Azure says about it.
        /// </summary>
        AzureRangeNotImported
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

        /// <summary>
        /// For <see cref="AzureReconcileStatus.RangeStillAllocatedInAzure"/>: the Azure subnet that
        /// now holds this row's range, so the operator can re-link in one click rather than typing a
        /// resource ID. Empty for every other status.
        /// </summary>
        /// <remarks>
        /// Offered by the server but never trusted from the client: the re-link endpoint re-derives
        /// this from a fresh scan before writing, because the browser is not the authority on what
        /// Azure holds.
        /// </remarks>
        public string SuggestedAzureResourceId { get; set; } = string.Empty;

        /// <summary>Display name of <see cref="SuggestedAzureResourceId"/>.</summary>
        public string SuggestedAzureSubnetName { get; set; } = string.Empty;

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

        /// <summary>
        /// The verdict the operator actually approved, one entry per selected subnet, snapshotted
        /// when the confirmation screen was built.
        /// </summary>
        /// <remarks>
        /// Set membership in the re-derived plan is not consent. A row approved under "the Azure
        /// resource no longer exists" can still be in the plan moments later under "the prefix
        /// changed" - a different fact, reached without any direct ARM read - and the subtree was
        /// archived on an approval whose stated premise the server had itself disproved. Required:
        /// a request that names no verdict is refused rather than trusted, because an omitted
        /// verdict is exactly what a replayed or hand-built post carries.
        /// </remarks>
        public List<AzureReconcileApprovedVerdict> Statuses { get; set; } = [];
    }

    /// <summary>One row's verdict as it was shown to the operator on the confirmation screen.</summary>
    public class AzureReconcileApprovedVerdict
    {
        public int SubnetId { get; set; }

        /// <summary>The <see cref="AzureReconcileStatus"/> name, as rendered.</summary>
        public string StatusName { get; set; } = string.Empty;

        /// <summary>
        /// The reason text shown beside the row. Compared as well as the status, because the same
        /// status can carry different facts - a prefix that moved again re-derives as
        /// SubnetPrefixChanged both times while naming a different live prefix.
        /// </summary>
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Re-points one Bastet subnet at the Azure subnet that now holds its range, after a rename or
    /// a prefix move left the recorded resource ID naming something that no longer exists.
    /// </summary>
    /// <remarks>
    /// Carries no resource ID on purpose. The server re-scans and derives the new link itself from
    /// the fresh plan, so a stale browser view - or a crafted post - cannot point a subnet at an
    /// arbitrary Azure resource. The client names the row; Azure decides what it links to.
    /// </remarks>
    public class AzureRelinkDto
    {
        public string SubscriptionId { get; set; } = string.Empty;

        /// <summary>The Bastet subnet to re-link.</summary>
        public int SubnetId { get; set; }
    }
}
