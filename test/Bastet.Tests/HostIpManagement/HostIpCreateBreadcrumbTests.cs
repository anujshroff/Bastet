using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.HostIpManagement;

public class HostIpCreateBreadcrumbTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private const string HeaderPartial = "src/Bastet/Views/HostIp/Create/_Header.cshtml";
    private const string FormPartial = "src/Bastet/Views/HostIp/Create/_HostIpForm.cshtml";

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Bastet.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Bastet.sln not found above test base directory");
    }

    private static string ReadView(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static HostIpController Controller(BastetDbContext context)
    {
        IIpUtilityService ipUtility = new IpUtilityService();
        HostIpController controller = new(
            context,
            new HostIpValidationService(ipUtility, context),
            ipUtility,
            ControllerTestHelper.CreateMockUserContextService(),
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<HostIpController>.Instance);
        ControllerTestHelper.SetupController(controller);
        return controller;
    }

    private static BastetDbContext SeededContext(string subnetName)
    {
        BastetDbContext context = TestDbContextFactory.CreateDbContext();
        context.Subnets.Add(new Subnet
        {
            Id = 1,
            Name = subnetName,
            NetworkAddress = "10.200.1.0",
            Cidr = 24,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        context.SaveChanges();
        return context;
    }

    [Theory]
    [InlineData("Lab (test)")]
    [InlineData("(alpha)")]
    [InlineData("rig-r31-a2 (10.32.0.0-16)")]
    [InlineData("Plain")]
    public async Task CreateGet_CarriesTheSubnetsOwnName_NotACutOfTheDisplayString(string subnetName)
    {
        using BastetDbContext context = SeededContext(subnetName);

        IActionResult result = await Controller(context).Create(1);

        ViewResult view = Assert.IsType<ViewResult>(result);
        CreateHostIpViewModel model = Assert.IsType<CreateHostIpViewModel>(view.Model);
        Assert.Equal(subnetName, model.SubnetName);
        Assert.Equal($"{subnetName} (10.200.1.0/24)", model.SubnetInfo);
    }

    [Fact]
    public async Task CreatePostRedisplay_KeepsTheSubnetName_SoTheBreadcrumbSurvivesAValidationFailure()
    {
        using BastetDbContext context = SeededContext("Lab (test)");

        CreateHostIpViewModel posted = new()
        {
            IP = "not-an-ip",
            SubnetId = 1,
            SubnetName = "Lab (test)"
        };

        HostIpController controller = Controller(context);
        controller.ModelState.AddModelError(nameof(CreateHostIpViewModel.IP), "Invalid IP address format");

        IActionResult result = await controller.Create(posted);

        ViewResult view = Assert.IsType<ViewResult>(result);
        CreateHostIpViewModel model = Assert.IsType<CreateHostIpViewModel>(view.Model);
        Assert.Equal("Lab (test)", model.SubnetName);
    }

    [Fact]
    public async Task CreatePostRedisplay_RestoresTheSubnetNameFromTheRow_NotFromWhateverWasPosted()
    {
        using BastetDbContext context = SeededContext("Lab (test)");

        CreateHostIpViewModel posted = new()
        {
            IP = "not-an-ip",
            SubnetId = 1,
            SubnetName = "tampered"
        };

        HostIpController controller = Controller(context);
        controller.ModelState.AddModelError(nameof(CreateHostIpViewModel.IP), "Invalid IP address format");

        IActionResult result = await controller.Create(posted);

        ViewResult view = Assert.IsType<ViewResult>(result);
        CreateHostIpViewModel model = Assert.IsType<CreateHostIpViewModel>(view.Model);
        Assert.Equal("Lab (test)", model.SubnetName);
    }

    [Fact]
    public void TheBreadcrumb_RendersTheNameProperty_AndNeverSplitsTheCompositeString()
    {
        string header = ReadView(HeaderPartial);

        Assert.Contains("@Model.SubnetName</a>", header);
        Assert.DoesNotContain("Split(", header);
        Assert.DoesNotContain("SubnetInfo.Split", header);
    }

    [Fact]
    public void TheForm_RoundTripsTheSubnetName_AlongsideTheOtherDisplayValues()
    {
        string form = ReadView(FormPartial);

        Assert.Contains("<input type=\"hidden\" asp-for=\"SubnetName\" />", form);
    }

    [Fact]
    public void NoViewInTheProduct_RecoversASubnetNameByCuttingADisplayString()
    {
        string viewsRoot = Path.Combine(RepoRoot, "src", "Bastet", "Views");

        List<string> offenders =
        [
            .. Directory.EnumerateFiles(viewsRoot, "*.cshtml", SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).Contains("Split('('"))
                .Select(path => Path.GetRelativePath(RepoRoot, path))
        ];

        Assert.Empty(offenders);
    }
}
