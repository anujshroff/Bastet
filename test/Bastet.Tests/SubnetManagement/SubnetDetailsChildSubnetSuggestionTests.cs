using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.SubnetManagement;

public class SubnetDetailsChildSubnetSuggestionTests
{
    private const int ParentId = 2;

    private static SubnetController CreateController(BastetDbContext context)
    {
        IIpUtilityService ip = new IpUtilityService();
        SubnetController controller = new(
            context,
            ip,
            new SubnetValidationService(ip),
            new HostIpValidationService(ip, context),
            ControllerTestHelper.CreateMockUserContextService(),
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<SubnetController>.Instance);
        ControllerTestHelper.SetupController(controller);
        return controller;
    }

    private static async Task<BastetDbContext> SeedTreeAsync()
    {
        BastetDbContext context = TestDbContextFactory.CreateDbContext();
        context.Subnets.AddRange(
            new Subnet { Id = 1, Name = "root", NetworkAddress = "10.0.0.0", Cidr = 8, CreatedAt = DateTime.UtcNow },
            new Subnet { Id = ParentId, Name = "parent", NetworkAddress = "10.0.0.0", Cidr = 16, ParentSubnetId = 1, CreatedAt = DateTime.UtcNow },
            new Subnet { Id = 3, Name = "first", NetworkAddress = "10.0.0.0", Cidr = 24, ParentSubnetId = ParentId, CreatedAt = DateTime.UtcNow },
            new Subnet { Id = 4, Name = "third", NetworkAddress = "10.0.2.0", Cidr = 23, ParentSubnetId = ParentId, CreatedAt = DateTime.UtcNow },
            new Subnet { Id = 5, Name = "sibling", NetworkAddress = "10.1.0.0", Cidr = 16, ParentSubnetId = 1, CreatedAt = DateTime.UtcNow },
            new Subnet { Id = 6, Name = "nephew", NetworkAddress = "10.1.0.0", Cidr = 24, ParentSubnetId = 5, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return context;
    }

    private static async Task<SubnetDetailsViewModel> DetailsAsync(BastetDbContext context, int id)
    {
        IActionResult result = await CreateController(context).Details(id);
        ViewResult view = Assert.IsType<ViewResult>(result);
        return Assert.IsType<SubnetDetailsViewModel>(view.Model);
    }

    [Fact]
    public async Task Details_StampsOneSuggestionPerUnallocatedRange_InRangeOrder()
    {
        using BastetDbContext context = await SeedTreeAsync();

        SubnetDetailsViewModel model = await DetailsAsync(context, ParentId);

        Assert.Equal(2, model.UnallocatedRanges.Count);
        Assert.Equal(model.UnallocatedRanges.Count, model.ChildSubnetSuggestions.Count);
        Assert.Equal(model.UnallocatedRanges.Select(r => r.StartIp), model.ChildSubnetSuggestions.Select(s => s.StartIp));

        ChildSubnetSuggestion gap = model.ChildSubnetSuggestions[0];
        Assert.Equal("10.0.1.0", gap.StartIp);
        Assert.Equal(24, gap.RecommendedCidr);
        Assert.Equal("10.0.1.0", gap.NetworkAddressByCidr[24]);
        Assert.Equal("10.0.4.0", gap.NetworkAddressByCidr[23]);
        Assert.Equal("10.0.128.0", gap.NetworkAddressByCidr[17]);
        Assert.False(gap.NetworkAddressByCidr.ContainsKey(16));

        ChildSubnetSuggestion tail = model.ChildSubnetSuggestions[1];
        Assert.Equal("10.0.4.0", tail.StartIp);
        Assert.Equal(22, tail.RecommendedCidr);
        Assert.Equal("10.0.128.0", tail.NetworkAddressByCidr[17]);
    }

    [Fact]
    public async Task Details_EverySuggestedBlock_IsAcceptedByCreate()
    {
        List<ChildSubnetSuggestion> suggestions;
        using (BastetDbContext context = await SeedTreeAsync())
        {
            SubnetDetailsViewModel model = await DetailsAsync(context, ParentId);
            Assert.Equal(model.UnallocatedRanges.Count, model.ChildSubnetSuggestions.Count);
            suggestions = model.ChildSubnetSuggestions;
        }

        List<(string StartIp, int Cidr, string Address)> offers = [.. suggestions
            .SelectMany(s => s.NetworkAddressByCidr
                .Where(kv => kv.Value != null)
                .Select(kv => (s.StartIp, kv.Key, kv.Value!)))];

        Assert.NotEmpty(offers);
        Assert.Contains(offers, o => o.Address != o.StartIp);

        foreach ((string startIp, int cidr, string address) in offers)
        {
            using BastetDbContext context = await SeedTreeAsync();
            SubnetController controller = CreateController(context);

            IActionResult result = await controller.Create(new CreateSubnetViewModel
            {
                Name = $"offer-{address}-{cidr}",
                NetworkAddress = address,
                Cidr = cidr,
                ParentSubnetId = ParentId
            });

            RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(SubnetController.Details), redirect.ActionName);
            Assert.True(controller.ModelState.IsValid,
                $"offer {address}/{cidr} from range {startIp} refused: {string.Join("; ", controller.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage))}");
        }
    }

    [Fact]
    public async Task Details_WithHostIps_StampsNoSuggestions()
    {
        using BastetDbContext context = TestDbContextFactory.CreateDbContext();
        context.Subnets.Add(new Subnet { Id = 1, Name = "hosts", NetworkAddress = "10.5.0.0", Cidr = 24, CreatedAt = DateTime.UtcNow });
        context.HostIpAssignments.Add(new HostIpAssignment { IP = "10.5.0.10", SubnetId = 1, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        SubnetDetailsViewModel model = await DetailsAsync(context, 1);

        Assert.False(model.CanAddChildSubnet);
        Assert.NotEmpty(model.UnallocatedRanges);
        Assert.Empty(model.ChildSubnetSuggestions);
    }

    [Fact]
    public async Task Details_SlashThirtyTwo_StampsNoSuggestions()
    {
        using BastetDbContext context = TestDbContextFactory.CreateDbContext();
        context.Subnets.Add(new Subnet { Id = 1, Name = "single", NetworkAddress = "10.6.0.1", Cidr = 32, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        SubnetDetailsViewModel model = await DetailsAsync(context, 1);

        Assert.False(model.CanAddChildSubnet);
        Assert.Empty(model.ChildSubnetSuggestions);
    }
}
