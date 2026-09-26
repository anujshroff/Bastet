using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.ComponentModel.DataAnnotations;
using ValidationContext = System.ComponentModel.DataAnnotations.ValidationContext;

namespace Bastet.Tests.Azure;

[Collection(AzureFeatureFlagCollection.Name)]
public class BulkImportNameParityTests : IDisposable
{
    private const string SubId = "33333333-3333-3333-3333-333333333333";
    private const string VNetId =
        $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/parity-vnet";

    private readonly BastetDbContext _context;
    private readonly SubnetController _controller;
    private readonly IAzureBulkImportPlanner _planner;
    private readonly IAzureSubnetSnapshotService _snapshotService;
    private readonly InputSanitizationService _sanitization = new();

    public BulkImportNameParityTests()
    {
        DbContextOptions<BastetDbContext> options = new DbContextOptionsBuilder<BastetDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new BastetDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        IIpUtilityService ipUtilityService = new IpUtilityService();
        _planner = new AzureBulkImportPlanner(ipUtilityService, _sanitization);
        _snapshotService = new AzureSubnetSnapshotService(_context);

        _controller = new SubnetController(
            _context,
            ipUtilityService,
            new SubnetValidationService(ipUtilityService),
            new HostIpValidationService(ipUtilityService, _context),
            ControllerTestHelper.CreateMockUserContextService(),
            ControllerTestHelper.CreateMockSubnetLockingService(),
            NullLogger<SubnetController>.Instance);
        ControllerTestHelper.SetupController(_controller);
        _controller.Url = Mock.Of<IUrlHelper>();

        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "true");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", null);
        _context.Database.CloseConnection();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private Task<IActionResult> Commit(BulkImportSelectionDto selection) =>
        _controller.BulkCreateFromAzurePlan(selection, _planner, _snapshotService, _sanitization);

    private static BulkImportSelectionDto SelectionWithChild(string childName) => new()
    {
        VNetPrefixes =
        [
            new BulkImportSelectedVNetPrefixDto
            {
                VNetName = "parity-vnet",
                VNetResourceId = VNetId,
                AddressPrefix = "10.151.0.0/16",
                Subnets =
                [
                    new BulkImportSelectedSubnetDto
                    {
                        Name = childName,
                        AddressPrefix = "10.151.1.0/24",
                        AzureResourceId = $"{VNetId}/subnets/{childName}"
                    }
                ],
                Expected = new BulkImportExpectedTargetDto
                {
                    TargetType = nameof(BulkImportTargetType.AutoCreateTopLevel),
                    ExistingTargetSubnetId = null,
                    AutoCreateParentSubnetId = null,
                    WillRename = false,
                    NewName = null,
                    WillMarkFullyAllocated = false
                }
            }
        ],
        RenameMatchedBastetSubnets = false
    };

    private static bool FormRefuses(string name)
    {
        CreateSubnetViewModel model = new()
        {
            Name = name,
            NetworkAddress = "10.151.1.0",
            Cidr = 24
        };

        List<System.ComponentModel.DataAnnotations.ValidationResult> failures = [];
        bool accepted = Validator.TryValidateProperty(
            model.Name,
            new ValidationContext(model, new SanitizationProvider(), null)
            {
                MemberName = nameof(CreateSubnetViewModel.Name)
            },
            failures);

        return !accepted;
    }

    [Theory]
    [InlineData("<b>s1</b> web")]
    [InlineData("Owner <jane@example.com>")]
    [InlineData("<script>alert(1)</script>")]
    public async Task ANameTheFormRefuses_IsAlsoRefusedByTheBulkCommit_AndNothingIsWritten(string childName)
    {
        Assert.True(FormRefuses(childName), "precondition: the manual form refuses this name");

        IActionResult result = await Commit(SelectionWithChild(childName));

        _ = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await _context.Subnets.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("s1")]
    [InlineData("web-01_prod.a")]
    public async Task ANameTheFormAccepts_IsStillImported(string childName)
    {
        Assert.False(FormRefuses(childName), "precondition: the manual form accepts this name");

        IActionResult result = await Commit(SelectionWithChild(childName));

        _ = Assert.IsType<OkObjectResult>(result);
        List<Subnet> written = await _context.Subnets.ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains(written, s => s.Name == childName);
    }

    [Fact]
    public async Task ATaggedVNetName_IsRefused_AndNothingIsWritten()
    {
        BulkImportSelectionDto selection = SelectionWithChild("s1");
        selection.VNetPrefixes[0].VNetName = "<b>parity-vnet</b>";

        IActionResult result = await Commit(selection);

        _ = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await _context.Subnets.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ATaggedRenameTarget_IsRefused_AndNothingIsWritten()
    {
        BulkImportSelectionDto selection = SelectionWithChild("s1");
        selection.VNetPrefixes[0].Expected!.WillRename = true;
        selection.VNetPrefixes[0].Expected!.NewName = "Owner <jane@example.com>";

        IActionResult result = await Commit(selection);

        _ = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await _context.Subnets.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARefusedName_IsNeverStoredStripped()
    {
        await Commit(SelectionWithChild("<b>s1</b> web"));

        List<Subnet> written = await _context.Subnets.ToListAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(written, s => s.Name == "s1 web");
    }

    [Theory]
    [InlineData("<b>s1</b>", true)]
    [InlineData("Owner <jane@example.com>", true)]
    [InlineData("temp < 5 and load > 3", false)]
    [InlineData("plain name", false)]
    public void TheHtmlQuestion_HasOneImplementation_SharedByBothWritePaths(string value, bool expected)
    {
        Assert.Equal(expected, _sanitization.ContainsHtmlTags(value));
        Assert.Equal(expected, FormRefuses(value));
    }

    private sealed class SanitizationProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IInputSanitizationService) ? new InputSanitizationService() : null;
    }
}
