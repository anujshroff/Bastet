using System.Text.RegularExpressions;

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

        Assert.DoesNotMatch(@"<<|>>>|Math\.pow|& ?255|\*\*|16777216|65536|split\('\.'\)|[*/%] ?256", script);
        Assert.Contains("childSubnetSuggestions", script);
        Assert.Contains("const address = activeSuggestion.networkAddressByCidr[cidrValue];", script);
        Assert.Contains("Model.ChildSubnetSuggestions", script);
        Assert.Contains("IpUtility.CalculateUsableIpAddresses", script);

        MatchCollection sizeWrites = Regex.Matches(script, @"#subnetSizeDisplay[""']\)\.text\(([^;]*)\);");
        Assert.Equal(3, sizeWrites.Count);
        Assert.All(sizeWrites, m => Assert.Matches(
            @"^(usableByCidr\[(activeSuggestion\.recommendedCidr|cidrValue)\]\.toLocaleString\(\)|sizeText)$",
            m.Groups[1].Value));
        Assert.Matches(@"if \(address === null\)\s*\{\s*refuse\(`No free /\$\{cidrValue\} block starts at or after \$\{activeSuggestion\.startIp\}\.`", script);
        Assert.Matches(@"if \(address === undefined\)\s*\{\s*refuse\('Please enter a valid CIDR value within the allowed range\.'", script);
        Assert.DoesNotContain("No compatible network address found", script);
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

    [Fact]
    public void CidrModalScript_ReadsTheCidrInputAsANumber()
    {
        string script = ReadView(ScriptPartial);

        Assert.Contains("const cidrValue = this.valueAsNumber;", script);
        Assert.Contains("cidr: $('#cidrInput').prop('valueAsNumber'),", script);
        Assert.DoesNotContain("parseInt(", script);
        Assert.DoesNotContain("$('#cidrInput').val()", script);

        string createFormScript = ReadView("src/Bastet/Views/Subnet/Create/_SubnetFormScripts.cshtml");
        Assert.Contains("const info = cidrInfo[$('#Cidr').prop('valueAsNumber')];", createFormScript);
        Assert.Contains("if (info !== undefined) {", createFormScript);
        Assert.DoesNotContain("parseInt(", createFormScript);
    }

    [Fact]
    public void CidrModalScript_RefuseResetsTheNetworkAddressToTheRangeStart()
    {
        string script = ReadView(ScriptPartial);

        Assert.Matches(@"function refuse\([^)]*\)\s*\{\s*\$\('#networkAddressDisplay'\)\.val\(activeSuggestion\.startIp\);\s*makeNetworkAddressReadOnly\(\);", script);
    }
}
