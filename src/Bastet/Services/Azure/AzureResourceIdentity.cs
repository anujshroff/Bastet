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
        private const string VNetResourceType = "Microsoft.Network/virtualNetworks";

        /// <summary>
        /// True when the ID names an Azure subnet rather than a VNet or anything else.
        /// </summary>
        /// <remarks>
        /// An ID that will not parse returns false. AzureResourceId is free text on the entity and
        /// can be edited by hand, so one bad row must not be able to abort a whole reconcile scan.
        /// Note this is only half the question: false does not mean "VNet". Callers that act on the
        /// answer must ask <see cref="IsAzureVNet"/> too and treat neither-of-the-two as unknown.
        /// </remarks>
        public static bool IsAzureSubnet(string? resourceId) =>
            IsResourceType(resourceId, SubnetResourceType);

        /// <summary>
        /// True when the ID names an Azure VNet.
        /// </summary>
        /// <remarks>
        /// Needed because the Azure SDK's VNet accessor builds its request from the subscription,
        /// the resource group and the **last path segment** only - it discards the provider
        /// namespace and type, and does not validate them. So reading a resource-group ID, a storage
        /// account ID or a truncated subnet ID through it silently asks about a different resource,
        /// and a 404 there is indistinguishable from the VNet genuinely being gone. Anything that is
        /// neither a VNet nor a subnet must be reported as unknown, never as deleted.
        /// </remarks>
        public static bool IsAzureVNet(string? resourceId) =>
            IsResourceType(resourceId, VNetResourceType);

        /// <summary>
        /// The VNet an ID belongs to: the parent for a subnet ID, itself for a VNet ID, null for
        /// anything else. Used to scope a range comparison to one VNet.
        /// </summary>
        /// <remarks>
        /// Scoping matters because overlapping RFC1918 space across unrelated VNets in one
        /// subscription is the norm, not an anomaly. Comparing bare prefix strings across the whole
        /// inventory would treat an unrelated VNet's 10.0.0.0/8 as evidence that a genuinely stale
        /// row's range is still allocated, and withhold a deletion that ought to be offered.
        /// </remarks>
        public static string? VNetIdOf(string? resourceId)
        {
            if (string.IsNullOrWhiteSpace(resourceId)
                || !ResourceIdentifier.TryParse(resourceId, out ResourceIdentifier? id)
                || id is null)
            {
                return null;
            }

            string type = id.ResourceType.ToString();

            if (string.Equals(type, VNetResourceType, StringComparison.OrdinalIgnoreCase))
            {
                return id.ToString();
            }

            return string.Equals(type, SubnetResourceType, StringComparison.OrdinalIgnoreCase)
                ? id.Parent?.ToString()
                : null;
        }

        private static bool IsResourceType(string? resourceId, string resourceType) =>
            !string.IsNullOrWhiteSpace(resourceId)
            && ResourceIdentifier.TryParse(resourceId, out ResourceIdentifier? id)
            && id is not null
            && string.Equals(id.ResourceType.ToString(), resourceType, StringComparison.OrdinalIgnoreCase);

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
