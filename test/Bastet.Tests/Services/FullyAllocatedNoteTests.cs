using Bastet.Services;

namespace Bastet.Tests.Services;

/// <summary>
/// The "fully allocated by Azure subnet ..." note. Appending it is deliberate and existing operator
/// text must never be sacrificed for it; what was missing is idempotence, so an ordinary
/// import - un-mark - import cycle stacked the identical sentence once per pass.
/// </summary>
public class FullyAllocatedNoteTests
{
    private const int Max = 1000;

    private static string Note(string name) => FullyAllocatedNote.For(name);

    // -------------------------------------------------------------------------
    // Idempotence - the defect
    // -------------------------------------------------------------------------

    /// <summary>Four import/un-mark cycles must leave exactly one note, not four.</summary>
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

    /// <summary>
    /// The gap the finder's own proposal had: exact equality against the current note would not
    /// dedupe a note written before the Azure subnet was renamed, so two distinct notes accumulated.
    /// </summary>
    [Fact]
    public void ANoteForADifferentlyNamedAzureSubnet_IsAlsoReplaced()
    {
        string d = FullyAllocatedNote.Append(null, "old-name", Max);
        d = FullyAllocatedNote.Append(d, "new-name", Max);

        Assert.Equal(Note("new-name"), d);
        Assert.DoesNotContain("old-name", d);
    }

    // -------------------------------------------------------------------------
    // Operator text is never destroyed - the contract the loose-match fix would have broken
    // -------------------------------------------------------------------------

    /// <summary>Operator prose survives an append, and sits above the note.</summary>
    [Fact]
    public void OperatorText_SurvivesTheAppend()
    {
        string d = FullyAllocatedNote.Append("Prod DMZ. Owner: netops.", "sn", Max);

        Assert.Equal($"Prod DMZ. Owner: netops.\n{Note("sn")}", d);
    }

    /// <summary>And survives repeated appends, still exactly once, still with one note.</summary>
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

    /// <summary>
    /// The reason the match is anchored at BOTH ends and whole-line: prose that merely resembles the
    /// note must not be deleted. A loose shape match here would silently destroy operator text.
    /// </summary>
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

    // -------------------------------------------------------------------------
    // Strip, used by the un-mark path
    // -------------------------------------------------------------------------

    [Fact]
    public void Strip_RemovesTheNoteAndLeavesTheRest()
        => Assert.Equal("Owner: netops.",
            FullyAllocatedNote.Strip($"Owner: netops.\n{Note("sn")}"));

    /// <summary>A description that was only ever the note strips to empty, so the row can be nulled.</summary>
    [Fact]
    public void Strip_OfANoteOnlyDescription_IsEmpty()
        => Assert.Equal(string.Empty, FullyAllocatedNote.Strip(Note("sn")));

    /// <summary>Several stacked notes - the state existing rows are already in - all go.</summary>
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

    // -------------------------------------------------------------------------
    // The overflow contract, unchanged
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the note will not fit, it is dropped and the existing text kept whole - overflowing the
    /// column fails the insert and rolls back the entire import behind a generic error.
    /// </summary>
    [Fact]
    public void WhenTheNoteWouldNotFit_ExistingTextIsKeptAndTheNoteDropped()
    {
        string big = new('x', 950);
        string result = FullyAllocatedNote.Append(big, "sn", Max);

        Assert.Equal(big, result);
        Assert.DoesNotContain("Fully allocated by Azure subnet", result);
    }

    /// <summary>
    /// Deduping frees room, so a description that had stacked notes and would previously have
    /// overflowed can now take the note. Strictly better than the old behaviour, never worse.
    /// </summary>
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

    /// <summary>A note longer than the cap on its own is truncated rather than overflowing.</summary>
    [Fact]
    public void ANoteLongerThanTheCap_IsTruncated()
    {
        string result = FullyAllocatedNote.Append(null, new string('n', 2000), Max);
        Assert.Equal(Max, result.Length);
    }

    /// <summary>First import onto an empty description is the note alone, exactly as before.</summary>
    [Fact]
    public void FirstImport_WritesTheNoteAlone()
        => Assert.Equal(Note("sn"), FullyAllocatedNote.Append(null, "sn", Max));
}
