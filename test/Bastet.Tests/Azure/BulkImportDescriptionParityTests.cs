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
using System.Text.RegularExpressions;
using ValidationContext = System.ComponentModel.DataAnnotations.ValidationContext;

namespace Bastet.Tests.Azure;

[Collection(AzureFeatureFlagCollection.Name)]
public partial class BulkImportDescriptionParityTests : IDisposable
{
    private const string SubId = "44444444-4444-4444-4444-444444444444";
    private const string VNetId =
        $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/enc-vnet";
    private const string Prefix = "10.15.0.0/24";
    private const string AzureSubnetName = "snet-all";
    private const int Cap = 1000;

    [GeneratedRegex(@"(?<!\r)\n")]
    private static partial Regex BareLineFeedPattern();

    private readonly BastetDbContext _context;
    private readonly SubnetController _controller;
    private readonly IAzureBulkImportPlanner _planner;
    private readonly IAzureSubnetSnapshotService _snapshotService;
    private readonly IInputSanitizationService _sanitization = new InputSanitizationService();

    public BulkImportDescriptionParityTests()
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

    private static int NoteLength => FullyAllocatedNote.For(AzureSubnetName).Length;

    private static string FormWrittenDescription(int length) =>
        ("Prod DMZ.\r\nOwner: netops.\r\n" + new string('x', 1100))[..length];

    private static string AsABrowserRePostsIt(string stored) => BareLineFeedPattern().Replace(stored, "\r\n");

    private static bool EditFormAccepts(string description)
    {
        EditSubnetViewModel model = new()
        {
            Name = "enc-hand",
            NetworkAddress = "10.15.0.0",
            Cidr = 24,
            Description = description
        };

        List<System.ComponentModel.DataAnnotations.ValidationResult> failures = [];
        return Validator.TryValidateProperty(
            model.Description,
            new ValidationContext(model, new SanitizationProvider(), null)
            {
                MemberName = nameof(EditSubnetViewModel.Description)
            },
            failures);
    }

    private async Task<Subnet> SeedHandMadeTarget(string description)
    {
        Subnet target = new()
        {
            Name = "enc-hand",
            NetworkAddress = "10.15.0.0",
            Cidr = 24,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _context.Subnets.Add(target);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return target;
    }

    private static BulkImportSelectionDto EncompassingSelection(int targetId) => new()
    {
        VNetPrefixes =
        [
            new BulkImportSelectedVNetPrefixDto
            {
                VNetName = "enc-vnet",
                VNetResourceId = VNetId,
                AddressPrefix = Prefix,
                VNetIpv4AddressPrefixes = [Prefix],
                Subnets =
                [
                    new BulkImportSelectedSubnetDto
                    {
                        Name = AzureSubnetName,
                        AddressPrefix = Prefix,
                        AzureResourceId = $"{VNetId}/subnets/{AzureSubnetName}",
                        Ipv4AddressPrefixes = [Prefix]
                    }
                ],
                Expected = new BulkImportExpectedTargetDto
                {
                    TargetType = nameof(BulkImportTargetType.ExactMatch),
                    ExistingTargetSubnetId = targetId,
                    AutoCreateParentSubnetId = null,
                    WillRename = false,
                    NewName = null,
                    WillMarkFullyAllocated = true,
                    ChildNames = []
                }
            }
        ],
        RenameMatchedBastetSubnets = false
    };

    private Task<IActionResult> Commit(BulkImportSelectionDto selection) =>
        _controller.BulkCreateFromAzurePlan(selection, _planner, _snapshotService, _sanitization);

    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(0)]
    public async Task ADescriptionTheImportWrites_IsAcceptedByTheEditForm_AsABrowserRePostsIt(int offsetFromTheLfBoundary)
    {
        string typed = FormWrittenDescription(Cap - NoteLength - 1 + offsetFromTheLfBoundary);
        Assert.True(EditFormAccepts(typed), "precondition: the operator's own description is accepted by the form");
        Subnet target = await SeedHandMadeTarget(typed);

        IActionResult result = await Commit(EncompassingSelection(target.Id));

        _ = Assert.IsType<OkObjectResult>(result);
        Subnet stored = await _context.Subnets.AsNoTracking().SingleAsync(s => s.Id == target.Id, TestContext.Current.CancellationToken);
        Assert.True(stored.IsFullyAllocated);
        Assert.NotNull(stored.Description);
        Assert.True(stored.Description.Length <= Cap);
        Assert.True(EditFormAccepts(AsABrowserRePostsIt(stored.Description)),
            $"the import stored {stored.Description.Length} characters that the Edit form re-posts as {AsABrowserRePostsIt(stored.Description).Length}");
    }

    [Fact]
    public async Task WhenTheNoteFits_TheImportStoresItWithTheLineBreakTheFormRePosts()
    {
        string typed = FormWrittenDescription(Cap - NoteLength - 2);
        Subnet target = await SeedHandMadeTarget(typed);

        _ = Assert.IsType<OkObjectResult>(await Commit(EncompassingSelection(target.Id)));

        Subnet stored = await _context.Subnets.AsNoTracking().SingleAsync(s => s.Id == target.Id, TestContext.Current.CancellationToken);
        Assert.Equal($"{typed}\r\n{FullyAllocatedNote.For(AzureSubnetName)}", stored.Description);
        Assert.Equal(Cap, stored.Description!.Length);
        Assert.Equal(stored.Description, AsABrowserRePostsIt(stored.Description));
    }

    private sealed class SanitizationProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IInputSanitizationService) ? new InputSanitizationService() : null;
    }
}
