using Bastet.Services;

namespace Bastet.Tests.Services;

public class FullyAllocatedNoteTests
{
    private const int Max = 1000;

    private static string Note(string name) => FullyAllocatedNote.For(name);

    [Fact]
    public void RepeatedAppends_LeaveExactlyOneNote()
    {
        string d = FullyAllocatedNote.Append(null, "sn", Max);
        for (int i = 0; i < 3; i++)
        {
            d = FullyAllocatedNote.Append(d, "sn", Max);
        }

        Assert.Equal(Note("sn"), d);
        Assert.Single(d.Split('\n'));
    }

    [Fact]
    public void ANoteForADifferentlyNamedAzureSubnet_IsAlsoReplaced()
    {
        string d = FullyAllocatedNote.Append(null, "old-name", Max);
        d = FullyAllocatedNote.Append(d, "new-name", Max);

        Assert.Equal(Note("new-name"), d);
        Assert.DoesNotContain("old-name", d);
    }

    [Fact]
    public void OperatorText_SurvivesTheAppend()
    {
        string d = FullyAllocatedNote.Append("Prod DMZ. Owner: netops.", "sn", Max);

        Assert.Equal($"Prod DMZ. Owner: netops.\n{Note("sn")}", d);
    }

    [Fact]
    public void OperatorText_SurvivesRepeatedAppends()
    {
        string d = "Prod DMZ. Owner: netops.";
        for (int i = 0; i < 4; i++)
        {
            d = FullyAllocatedNote.Append(d, "sn", Max);
        }

        Assert.Equal($"Prod DMZ. Owner: netops.\n{Note("sn")}", d);
    }

    [Theory]
    [InlineData("Fully allocated by Azure subnet 'sn' which encompasses the entire address space, per ticket 42.")]
    [InlineData("Note: fully allocated by Azure subnet 'sn' which encompasses the entire address space.")]
    [InlineData("Fully allocated by hand.")]
    [InlineData("which encompasses the entire address space.")]
    public void ProseThatMerelyResemblesTheNote_IsKept(string prose)
    {
        Assert.Equal(prose, FullyAllocatedNote.Strip(prose));

        string appended = FullyAllocatedNote.Append(prose, "sn", Max);
        Assert.Contains(prose, appended);
        Assert.EndsWith(Note("sn"), appended);
    }

    [Fact]
    public void Strip_RemovesTheNoteAndLeavesTheRest()
        => Assert.Equal("Owner: netops.",
            FullyAllocatedNote.Strip($"Owner: netops.\n{Note("sn")}"));

    [Fact]
    public void Strip_OfANoteOnlyDescription_IsEmpty()
        => Assert.Equal(string.Empty, FullyAllocatedNote.Strip(Note("sn")));

    [Fact]
    public void Strip_RemovesEveryStackedNote()
    {
        string stacked = string.Join('\n', Enumerable.Repeat(Note("sn"), 4));
        Assert.Equal(string.Empty, FullyAllocatedNote.Strip(stacked));

        string withText = "Owner: netops.\n" + stacked;
        Assert.Equal("Owner: netops.", FullyAllocatedNote.Strip(withText));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Strip_OfNothing_IsEmpty(string? input)
        => Assert.Equal(string.Empty, FullyAllocatedNote.Strip(input));

    [Fact]
    public void WhenTheNoteWouldNotFit_ExistingTextIsKeptAndTheNoteDropped()
    {
        string big = new('x', 950);
        string result = FullyAllocatedNote.Append(big, "sn", Max);

        Assert.Equal(big, result);
        Assert.DoesNotContain("Fully allocated by Azure subnet", result);
    }

    [Fact]
    public void StrippingStaleNotes_CanFreeEnoughRoomForTheNote()
    {
        string text = new('x', 850);
        string stacked = text + "\n" + string.Join('\n', Enumerable.Repeat(Note("sn"), 3));
        Assert.True(stacked.Length > Max);

        string result = FullyAllocatedNote.Append(stacked, "sn", Max);

        Assert.True(result.Length <= Max);
        Assert.StartsWith(text, result);
        Assert.EndsWith(Note("sn"), result);
        Assert.Equal(2, result.Split('\n').Length);
    }

    [Fact]
    public void ANoteLongerThanTheCap_IsTruncated()
    {
        string result = FullyAllocatedNote.Append(null, new string('n', 2000), Max);
        Assert.Equal(Max, result.Length);
    }

    [Fact]
    public void FirstImport_WritesTheNoteAlone()
        => Assert.Equal(Note("sn"), FullyAllocatedNote.Append(null, "sn", Max));

    [Theory]
    [InlineData("sn-A\nsn-B")]
    [InlineData("sn-A\r\nsn-B")]
    [InlineData("sn-A\rsn-B")]
    public void ANameCarryingALineBreak_StillProducesAStrippableNote(string name)
    {
        string note = FullyAllocatedNote.Append(null, name, Max);

        Assert.Single(note.Split('\n'));
        Assert.Equal(string.Empty, FullyAllocatedNote.Strip(note));
    }

    [Fact]
    public void RepeatedAppendsWithALineBrokenName_StillLeaveExactlyOneNote()
    {
        string d = FullyAllocatedNote.Append("Ops owns this range", "sn-A\nsn-B", Max);
        for (int i = 0; i < 3; i++)
        {
            d = FullyAllocatedNote.Append(d, "sn-A\nsn-B", Max);
        }

        Assert.Equal(2, d.Split('\n').Length);
        Assert.Equal("Ops owns this range", FullyAllocatedNote.Strip(d));
    }

    [Fact]
    public void TheNameSurvivesWithItsLineBreakCollapsedToASpace()
        => Assert.Equal(Note("sn-A sn-B"), FullyAllocatedNote.For("sn-A\nsn-B"));
}
