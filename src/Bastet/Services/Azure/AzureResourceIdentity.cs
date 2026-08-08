using Azure.Core;

namespace Bastet.Services.Azure
{

    public static class AzureResourceIdentity
    {
        private const string SubnetResourceType = "Microsoft.Network/virtualNetworks/subnets";
        private const string VNetResourceType = "Microsoft.Network/virtualNetworks";

        public static bool IsAzureSubnet(string? resourceId) =>
            IsResourceType(resourceId, SubnetResourceType);

        public static bool IsAzureVNet(string? resourceId) =>
            IsResourceType(resourceId, VNetResourceType);

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

        public static string ToPortalPath(string resourceId) =>
            IsAzureSubnet(resourceId) && ResourceIdentifier.TryParse(resourceId, out ResourceIdentifier? id) && id?.Parent is not null
                ? $"{id.Parent}/subnets"
                : resourceId;
    }
}
