using Azure.Core;

namespace Bastet.Services.Azure
{

    public static class AzureResourceIdentity
    {
        private const string SubnetResourceType = "Microsoft.Network/virtualNetworks/subnets";
        private const string VNetResourceType = "Microsoft.Network/virtualNetworks";

        public static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;

        public static bool IsSameResourceId(string? a, string? b) =>
            !string.IsNullOrEmpty(a)
            && !string.IsNullOrEmpty(b)
            && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        public static bool IsAzureSubnet(string? resourceId) =>
            IsResourceType(resourceId, SubnetResourceType);

        public static bool IsAzureVNet(string? resourceId) =>
            IsResourceType(resourceId, VNetResourceType);

        private static bool IsResourceType(string? resourceId, string resourceType) =>
            !string.IsNullOrWhiteSpace(resourceId)
            && ResourceIdentifier.TryParse(resourceId, out ResourceIdentifier? id)
            && id is not null
            && string.Equals(id.ResourceType.ToString(), resourceType, StringComparison.OrdinalIgnoreCase);

        public static string ToPortalPath(string resourceId) =>
            IsAzureSubnet(resourceId) && ResourceIdentifier.TryParse(resourceId, out ResourceIdentifier? id) && id?.Parent is not null
                ? $"{id.Parent}/subnets"
                : resourceId;
    }
}
