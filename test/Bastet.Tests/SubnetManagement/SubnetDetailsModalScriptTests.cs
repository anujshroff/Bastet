namespace Bastet.Tests.SubnetManagement;

public class SubnetDetailsModalScriptTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private const string ScriptPartial = "src/Bastet/Views/Subnet/Details/_SubnetCalculationScripts.cshtml";
    private const string ModalPartial = "src/Bastet/Views/Subnet/Details/_CidrInputModal.cshtml";
    private const string RangesPartial = "src/Bastet/Views/Subnet/Details/_UnallocatedRanges.cshtml";
    private const string ChildrenPartial = "src/Bastet/Views/Subnet/Details/_ChildSubnets.cshtml";

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

    public static TheoryData<string> ClientArithmeticIdentifiers => new()
    {
        "ipAddressToNumber",
        "numberToIpAddress",
        "normalizeIpToSubnetBoundary",
        "getSubnetBoundaries",
        "checkForOverlap",
        "findOptimalCidr",
        "findCompatibleNetworkAddress",
        "const childSubnets =",
        "SUBNET DEBUG",
        "Would overlap",
    };

    [Theory]
    [MemberData(nameof(ClientArithmeticIdentifiers))]
    public void CidrModalScript_CarriesNoIpArithmetic(string identifier) =>
        Assert.DoesNotContain(identifier, ReadView(ScriptPartial));

    [Fact]
    public void CidrModalScript_OnlyIndexesTheServerSuggestionTable()
    {
        string script = ReadView(ScriptPartial);

        Assert.DoesNotMatch(@"<<|>>>|Math\.pow|& ?255", script);
        Assert.Contains("childSubnetSuggestions", script);
        Assert.Contains("networkAddressByCidr", script);
        Assert.Contains("Model.ChildSubnetSuggestions", script);
        Assert.Contains("IpUtility.CalculateUsableIpAddresses", script);
        Assert.Contains("No compatible network address found for this CIDR size.", script);
        Assert.Contains("This network address has been adjusted to avoid overlaps.", script);
    }

    [Fact]
    public void CidrModalScript_BuildsTheCreateUrlFromTheRouteTable_AndEncodesEveryValue()
    {
        string script = ReadView(ScriptPartial);

        Assert.Contains("Url.Action(\"Create\", \"Subnet\")", script);
        Assert.Contains("new URLSearchParams(", script);
        Assert.DoesNotContain("/Subnet/Create", script);
        Assert.DoesNotMatch(@"location\.href\s*=\s*`", script);
    }

    [Fact]
    public void CidrModal_HasNoClientOnlyStateInputs()
    {
        string modal = ReadView(ModalPartial);

        Assert.DoesNotContain("type=\"hidden\"", modal);
        Assert.Contains("id=\"networkAddressDisplay\"", modal);
        Assert.Contains("id=\"cidrInput\"", modal);
    }

    [Fact]
    public void UnallocatedRangesButton_CarriesOnlyTheRangeStart()
    {
        string ranges = ReadView(RangesPartial);

        Assert.Contains("data-network=\"@range.StartIp\"", ranges);
        Assert.DoesNotContain("data-parent-id", ranges);
        Assert.DoesNotContain("data-parent-cidr", ranges);
    }

    [Fact]
    public void ChildSubnetsHeader_HasNoSecondEntryPointToCreate()
    {
        string children = ReadView(ChildrenPartial);

        Assert.DoesNotContain("asp-action=\"Create\"", children);
        Assert.DoesNotContain("Add Child Subnet", children);
        Assert.Contains("Fully Allocated", children);
        Assert.Contains("Has Host IPs", children);
        Assert.Contains("No child subnets have been created yet.", children);
    }
}
