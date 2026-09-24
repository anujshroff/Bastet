using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

public class AzureBulkImportAdoptionReasonTests
{
    private const string VNetId = "/subscriptions/test/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-a";

    private readonly AzureBulkImportPlanner _planner =
        new(new IpUtilityService(), new InputSanitizationService());

    private static BulkAzureVNetViewModel VNetWithAnUnimportedSubnet() =>
        new()
        {
            ResourceId = VNetId,
            Name = "vnet-a",
            Ipv4AddressPrefixes = ["10.34.0.0/16"],
            Subnets =
            [
                new BulkAzureSubnetViewModel
                {
                    ResourceId = $"{VNetId}/subnets/snet-new",
                    Name = "snet-new",
                    AddressPrefix = "10.34.9.0/24",
                    Ipv4AddressPrefixes = ["10.34.9.0/24"]
                }
            ]
        };

    private BulkAzurePrefixViewModel AnnotatedPrefix(string? targetLink, bool targetHasChildren)
    {
        BulkAzureVNetViewModel vnet = VNetWithAnUnimportedSubnet();
        _planner.AnnotateAvailability([vnet],
        [
            new ExistingSubnetSnapshot
            {
                Id = 1,
                Name = "hand-a",
                NetworkAddress = "10.34.0.0",
                Cidr = 16,
                HasChildSubnets = targetHasChildren,
                AzureResourceId = targetLink
            }
        ]);
        return Assert.Single(vnet.Prefixes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUnlinkedRowTheVNetWouldAdopt_IsDescribedAsAnImportIntoIt_NeverAsATopUp(bool targetHasChildren)
    {
        BulkAzurePrefixViewModel prefix = AnnotatedPrefix(targetLink: null, targetHasChildren);

        Assert.Equal(BulkImportAvailability.WillUpdateExisting, prefix.Status);
        Assert.Equal("Will import into existing Bastet subnet 'hand-a'.", prefix.Reason);
    }

    [Fact]
    public void ALinkedRowWithChildren_IsStillDescribedAsATopUp()
    {
        BulkAzurePrefixViewModel prefix = AnnotatedPrefix(targetLink: VNetId, targetHasChildren: true);

        Assert.Equal(BulkImportAvailability.WillUpdateExisting, prefix.Status);
        Assert.Equal(
            "Will add any missing subnets to existing Bastet subnet 'hand-a'. Subnets already imported are left untouched.",
            prefix.Reason);
    }
}
