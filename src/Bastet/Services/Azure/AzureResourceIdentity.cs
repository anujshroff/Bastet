using Azure.Core;

namespace Bastet.Services.Azure
{
    /// <summary>
    /// Anchored tests over stored ARM resource IDs, shared by everything that needs to tell an
    /// Azure subnet's ID from its VNet's.
    /// </summary>
    /// <remarks>
    /// Searching the path for a "/subnets/" segment does not work: resource group names are
    /// arbitrary alphanumerics, so a group named "subnets" puts that segment into the ID of every
    /// VNet underneath it. The reconciler then routes a live VNet down the subnet branch, finds no
    /// matching subnet, and reports it as deleted - offering a healthy VNet and its children for
    /// archival. Comparing the parsed resource *type* cannot be fooled by a name.
    /// Kept in one place so the two callers cannot drift apart again.
    /// </remarks>
    public static class AzureResourceIdentity
    {
        private const string SubnetResourceType = "Microsoft.Network/virtualNetworks/subnets";

        /// <summary>
        /// True when the ID names an Azure subnet rather than a VNet or anything else.
        /// </summary>
        /// <remarks>
        /// An ID that will not parse returns false, so callers treat it as VNet-level - the branch
        /// an unrecognised value already took. AzureResourceId is free text on the entity and can be
        /// edited by hand, and ResourceIdentifier's constructor throws on malformed input, so one
        /// bad row must not be able to abort a whole reconcile scan.
        /// </remarks>
        public static bool IsAzureSubnet(string? resourceId) =>
            !string.IsNullOrWhiteSpace(resourceId)
            && ResourceIdentifier.TryParse(resourceId, out ResourceIdentifier? id)
            && id is not null
            && string.Equals(id.ResourceType.ToString(), SubnetResourceType, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The path to link to in the Azure portal. Per-subnet portal pages are nearly empty, so a
        /// subnet ID resolves to its parent VNet's subnet list, which renders the useful table.
        /// Anything else is linked directly.
        /// </summary>
        public static string ToPortalPath(string resourceId) =>
            IsAzureSubnet(resourceId) && ResourceIdentifier.TryParse(resourceId, out ResourceIdentifier? id) && id?.Parent is not null
                ? $"{id.Parent}/subnets"
                : resourceId;
    }
}
