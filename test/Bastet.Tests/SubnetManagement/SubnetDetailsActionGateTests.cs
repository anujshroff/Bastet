using Bastet.Models.ViewModels;

namespace Bastet.Tests.SubnetManagement;

public class SubnetDetailsActionGateTests
{
    private static SubnetDetailsViewModel Details(int cidr, int hostIps = 0, int children = 0, bool fullyAllocated = false)
    {
        SubnetDetailsViewModel model = new()
        {
            Id = 1,
            Name = "gate",
            NetworkAddress = "10.0.0.0",
            Cidr = cidr,
            IsFullyAllocated = fullyAllocated
        };
        for (int i = 0; i < hostIps; i++)
        {
            model.HostIpAssignments.Add(new HostIpViewModel());
        }

        for (int i = 0; i < children; i++)
        {
            model.ChildSubnets.Add(new SubnetViewModel());
        }

        return model;
    }

    [Theory]
    [InlineData(24)]
    [InlineData(31)]
    public void ASubnetWithRoomForAChild_OffersAddChildSubnet(int cidr) =>
        Assert.True(Details(cidr).CanAddChildSubnet);

    [Fact]
    public void ASlash32_NeverOffersAddChildSubnet() =>
        Assert.False(Details(32).CanAddChildSubnet);

    [Fact]
    public void ASlash32_CanStillBeMarkedFullyAllocated()
    {
        SubnetDetailsViewModel model = Details(32);
        Assert.True(model.HostIpAssignments.Count == 0 && model.ChildSubnets.Count == 0 && !model.IsFullyAllocated);
    }

    [Fact]
    public void HostIps_StillBlockAddChildSubnet() =>
        Assert.False(Details(24, hostIps: 1).CanAddChildSubnet);

    [Fact]
    public void FullAllocation_StillBlocksAddChildSubnet() =>
        Assert.False(Details(24, fullyAllocated: true).CanAddChildSubnet);
}
