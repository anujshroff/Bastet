namespace Bastet.Models.ViewModels
{
    /// <summary>
    /// Lightweight Azure subnet info used in the Bulk Azure Import flow.
    /// IPv4-only.
    /// </summary>
    public class BulkAzureSubnetViewModel
    {
        /// <summary>
        /// The Azure resource ID of the subnet
        /// </summary>
        public string ResourceId { get; set; } = string.Empty;

        /// <summary>
        /// The Azure-given name of the subnet
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The IPv4 address prefix in CIDR notation (e.g. "10.0.1.0/24")
        /// </summary>
        public string AddressPrefix { get; set; } = string.Empty;
    }

    /// <summary>
    /// Lightweight Azure VNet info used in the Bulk Azure Import flow.
    /// Only IPv4 prefixes and IPv4 subnets are reported.
    /// </summary>
    public class BulkAzureVNetViewModel
    {
        /// <summary>
        /// Azure resource ID of the VNet
        /// </summary>
        public string ResourceId { get; set; } = string.Empty;

        /// <summary>
        /// Azure-given name of the VNet
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The VNet's IPv4 address prefixes (CIDR notation)
        /// </summary>
        public List<string> Ipv4AddressPrefixes { get; set; } = [];

        /// <summary>
        /// IPv4 subnets contained in this VNet
        /// </summary>
        public List<BulkAzureSubnetViewModel> Subnets { get; set; } = [];
    }

    /// <summary>
    /// View model for the initial Bulk Azure Import landing page
    /// </summary>
    public class BulkImportInitialViewModel
    {
        /// <summary>
        /// True when the bulk-import feature flag is enabled
        /// </summary>
        public bool IsFeatureEnabled { get; set; }
    }

    /// <summary>
    /// A single Azure subnet selection submitted by the browser
    /// </summary>
    public class BulkImportSelectedSubnetDto
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The IPv4 address prefix in CIDR notation (e.g. "10.0.1.0/24")
        /// </summary>
        public string AddressPrefix { get; set; } = string.Empty;

        /// <summary>
        /// The Azure resource ID of the subnet (round-tripped through the browser
        /// so we can persist it onto the imported Bastet subnet without re-querying Azure).
        /// </summary>
        public string AzureResourceId { get; set; } = string.Empty;
    }

    /// <summary>
    /// A single VNet IPv4 prefix selection submitted by the browser, plus its
    /// chosen child Azure subnets.
    /// </summary>
    public class BulkImportSelectedVNetPrefixDto
    {
        /// <summary>
        /// The Azure VNet's display name
        /// </summary>
        public string VNetName { get; set; } = string.Empty;

        /// <summary>
        /// The Azure VNet's resource id (used for disambiguation)
        /// </summary>
        public string VNetResourceId { get; set; } = string.Empty;

        /// <summary>
        /// The IPv4 prefix in CIDR notation (e.g. "10.0.0.0/16").
        /// One Bastet subnet target is produced per prefix.
        /// </summary>
        public string AddressPrefix { get; set; } = string.Empty;

        /// <summary>
        /// Selected Azure subnets that fall under this VNet prefix
        /// </summary>
        public List<BulkImportSelectedSubnetDto> Subnets { get; set; } = [];
    }

    /// <summary>
    /// Selection submitted by the browser to Preview / Commit endpoints
    /// </summary>
    public class BulkImportSelectionDto
    {
        public string SubscriptionId { get; set; } = string.Empty;

        public string? SubscriptionName { get; set; }

        /// <summary>
        /// Selected VNet IPv4 prefixes (each is an independent target)
        /// </summary>
        public List<BulkImportSelectedVNetPrefixDto> VNetPrefixes { get; set; } = [];

        /// <summary>
        /// Global checkbox: when true, all exact-match targets are renamed to the VNet name.
        /// Auto-created targets are always named after their VNet (the checkbox does not affect them).
        /// </summary>
        public bool RenameMatchedBastetSubnets { get; set; }
    }

    /// <summary>
    /// Snapshot of an existing Bastet subnet, used by the planner without
    /// requiring an EF entity. Decouples the planner from the database.
    /// </summary>
    public class ExistingSubnetSnapshot
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string NetworkAddress { get; set; } = string.Empty;
        public int Cidr { get; set; }
        public bool HasChildSubnets { get; set; }
        public bool HasHostIpAssignments { get; set; }
        public bool IsFullyAllocated { get; set; }
    }

    /// <summary>
    /// How the planner decided the target Bastet subnet for a VNet prefix
    /// </summary>
    public enum BulkImportTargetType
    {
        /// <summary>
        /// An existing Bastet subnet exactly matches the VNet prefix
        /// </summary>
        ExactMatch,

        /// <summary>
        /// No exact match exists; we will auto-create a new Bastet subnet
        /// as a child of the deepest existing Bastet subnet that contains the VNet prefix
        /// </summary>
        AutoCreateChild,

        /// <summary>
        /// No exact match and no containing Bastet subnet exists; we will auto-create
        /// a new top-level Bastet subnet for the VNet prefix
        /// </summary>
        AutoCreateTopLevel
    }

    /// <summary>
    /// Planned creation of a child Azure subnet under a target
    /// </summary>
    public class BulkImportPlannedChildSubnet
    {
        /// <summary>
        /// The original Azure subnet name (raw)
        /// </summary>
        public string OriginalAzureName { get; set; } = string.Empty;

        /// <summary>
        /// Final Bastet name (truncated, sanitized, possibly disambiguated)
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// IPv4 network address
        /// </summary>
        public string NetworkAddress { get; set; } = string.Empty;

        public int Cidr { get; set; }

        /// <summary>
        /// True when this Azure subnet's prefix exactly equals its VNet prefix.
        /// In that case we do NOT create the child; instead the target is marked IsFullyAllocated.
        /// </summary>
        public bool FullyEncompassesTarget { get; set; }

        /// <summary>
        /// The Azure resource ID of the source subnet (forwarded from the selection
        /// so the commit step can persist it onto the new Bastet subnet).
        /// </summary>
        public string AzureResourceId { get; set; } = string.Empty;
    }

    /// <summary>
    /// One row of the bulk import plan: maps a VNet prefix → target Bastet subnet → child Azure subnets
    /// </summary>
    public class BulkImportPlanItem
    {
        public string VNetName { get; set; } = string.Empty;
        public string VNetResourceId { get; set; } = string.Empty;

        /// <summary>
        /// Original VNet IPv4 prefix in CIDR notation
        /// </summary>
        public string VNetPrefix { get; set; } = string.Empty;

        public string PrefixNetworkAddress { get; set; } = string.Empty;
        public int PrefixCidr { get; set; }

        public BulkImportTargetType TargetType { get; set; }

        // Populated when TargetType = ExactMatch
        public int? ExistingTargetSubnetId { get; set; }
        public string? ExistingTargetSubnetName { get; set; }

        // Populated when TargetType = AutoCreateChild (the existing Bastet subnet under which we'll create the new target)
        public int? AutoCreateParentSubnetId { get; set; }
        public string? AutoCreateParentSubnetName { get; set; }

        /// <summary>
        /// For AutoCreate*: the planned name of the new Bastet target subnet
        /// </summary>
        public string? AutoCreateTargetName { get; set; }

        /// <summary>
        /// True when an exact-match target will be renamed to the VNet name
        /// (driven by the global RenameMatchedBastetSubnets flag)
        /// </summary>
        public bool WillRename { get; set; }

        /// <summary>
        /// The proposed new name when WillRename is true
        /// </summary>
        public string? NewName { get; set; }

        /// <summary>
        /// True when one of the selected Azure subnets fully encompasses the VNet prefix.
        /// In that case the target is marked IsFullyAllocated and no child subnets are created.
        /// </summary>
        public bool WillMarkFullyAllocated { get; set; }

        public string? FullyAllocatingAzureSubnetName { get; set; }

        /// <summary>
        /// Planned child subnet creations (excluding any that fully-encompass the target)
        /// </summary>
        public List<BulkImportPlannedChildSubnet> ChildSubnets { get; set; } = [];

        /// <summary>
        /// Hard errors specific to this plan item; presence blocks commit
        /// </summary>
        public List<string> Errors { get; set; } = [];

        /// <summary>
        /// Informational messages (currently unused — every issue is a hard error per design)
        /// </summary>
        public List<string> Warnings { get; set; } = [];
    }

    /// <summary>
    /// Full output of the planner, also used by the preview view and posted back on commit
    /// </summary>
    public class BulkImportPlanViewModel
    {
        public string SubscriptionId { get; set; } = string.Empty;
        public string? SubscriptionName { get; set; }
        public bool RenameMatchedBastetSubnets { get; set; }

        public List<BulkImportPlanItem> Items { get; set; } = [];

        /// <summary>
        /// Hard errors that span multiple items (overlap detection, etc.)
        /// </summary>
        public List<string> GlobalErrors { get; set; } = [];

        /// <summary>
        /// True only when there are no global errors and no per-item errors
        /// </summary>
        public bool CanCommit => GlobalErrors.Count == 0 && Items.All(i => i.Errors.Count == 0);
    }
}
