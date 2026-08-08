using Bastet.Services;
using Bastet.Services.Security;

namespace Bastet.Tests.Services;

public class SubnetNamingSafeTextTests
{
    private readonly InputSanitizationService _sanitizer = new();

    [Fact]
    public void ToSafeText_KeepsExactlyTheCharactersSafeTextAccepts()
    {
        List<char> disagreements = [];

        for (int c = 32; c < 127; c++)
        {
            char ch = (char)c;

            string probe = $"a{ch}a";
            bool acceptedByRule = _sanitizer.IsSafeText(probe);
            bool keptByFilter = SubnetNaming.ToSafeText(probe) == probe;

            if (acceptedByRule != keptByFilter)
            {
                disagreements.Add(ch);
            }
        }

        Assert.True(disagreements.Count == 0,
            "ToSafeText and IsSafeText disagree on: " + string.Join(" ", disagreements));
    }

    [Theory]
    [InlineData("Prod/Web", "ProdWeb")]
    [InlineData("Bob's Lab", "Bobs Lab")]
    [InlineData("DC1:Core", "DC1Core")]
    [InlineData("Plain Name", "Plain Name")]
    [InlineData("<b>markup</b>", "bmarkupb")]
    public void ToSafeText_DropsOnlyTheForbiddenCharacters(string input, string expected) =>
        Assert.Equal(expected, SubnetNaming.ToSafeText(input));

    [Theory]
    [InlineData("/ / /")]
    [InlineData("///")]
    [InlineData("  ")]
    [InlineData("")]
    [InlineData(null)]
    public void ToSafeText_ReturnsEmptyWhenNothingUsableSurvives(string? input) =>
        Assert.Equal(string.Empty, SubnetNaming.ToSafeText(input));

    [Fact]
    public void ToSafeText_OnlyEverShortens()
    {

        foreach (string sample in new[] { "Prod/Web", "a", "", "!!!", new string('x', 200) })
        {
            Assert.True(SubnetNaming.ToSafeText(sample).Length <= sample.Length);
        }
    }
}
