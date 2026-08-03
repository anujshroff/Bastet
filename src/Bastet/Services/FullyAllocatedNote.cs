namespace Bastet.Services;

/// <summary>
/// The sentence an Azure import writes into a subnet's description when an Azure subnet covers the
/// target's whole prefix, and the rules for keeping exactly one of it.
/// </summary>
/// <remarks>
/// Appending was always deliberate - the note explains a state the operator did not set by hand, and
/// existing text is never sacrificed for it. What was missing is idempotence: the wizard is reachable
/// again after the Details page's own "Mark as Not Fully Allocated", so an ordinary
/// import - un-mark - import cycle concatenated the identical sentence once per pass until the
/// description hit its cap.
///
/// Shared rather than private to the import controller because clearing the flag has to strip the
/// note too: leaving it behind is a description asserting a state the row no longer has.
/// </remarks>
public static class FullyAllocatedNote
{
    private const string Prefix = "Fully allocated by Azure subnet '";
    private const string Suffix = "' which encompasses the entire address space.";

    /// <summary>The note for a given Azure subnet name.</summary>
    /// <remarks>
    /// Line breaks in the name are collapsed to spaces, which is what makes every note single-line
    /// BY CONSTRUCTION. <see cref="Strip"/> matches whole lines anchored at both ends - deliberately,
    /// so operator prose that merely resembles the note is never destroyed - and a name carrying a
    /// newline produced a note spanning two lines, neither of which satisfies both anchors. No later
    /// Strip could remove it, so the flag could be cleared while the description went on asserting it
    /// and every import/un-mark cycle stacked another copy.
    ///
    /// Fixed here rather than at the two producers: this is the single choke point both call sites go
    /// through, and leaving the helper able to build an unstrippable note for any future caller is
    /// the same class of latent defect. Not fixed by tightening [SafeText] either - that class is
    /// shared with host and subnet names across the app.
    /// </remarks>
    public static string For(string? azureSubnetName) =>
        $"{Prefix}{Normalise(azureSubnetName)}{Suffix}";

    /// <summary>Collapses every line-break form to a space so the note cannot span lines.</summary>
    private static string? Normalise(string? azureSubnetName) =>
        azureSubnetName?.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

    /// <summary>
    /// <paramref name="description"/> with every fully-allocated note removed, whatever Azure subnet
    /// name it carries.
    /// </summary>
    /// <remarks>
    /// Whole-line, ordinal, anchored at both ends. Deliberately NOT a loose shape match: a pattern
    /// broad enough to catch prose that merely resembles the note can delete operator-authored text,
    /// which would break the "existing text is never sacrificed" contract this type exists to keep.
    /// Matching both ends also catches a note written for a since-renamed Azure subnet, which exact
    /// equality against the current note would miss.
    /// </remarks>
    public static string Strip(string? description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return string.Empty;
        }

        IEnumerable<string> kept = description
            .Split('\n')
            .Where(line => !IsNote(line));

        return string.Join('\n', kept).Trim('\n');
    }

    /// <summary>
    /// <paramref name="existingDescription"/> carrying exactly one note for
    /// <paramref name="azureSubnetName"/>, with any earlier note removed first.
    /// </summary>
    /// <remarks>
    /// The overflow contract is unchanged and is the reason this is not simply a concatenation: if
    /// the result would exceed <paramref name="maxLength"/> the note is dropped and the existing text
    /// kept, because overflowing the column fails the insert and rolls back the whole import behind a
    /// generic error. Stripping first means a description that used to overflow may now fit.
    /// </remarks>
    public static string Append(string? existingDescription, string? azureSubnetName, int maxLength)
    {
        string note = For(azureSubnetName);
        string existing = Strip(existingDescription);

        if (string.IsNullOrEmpty(existing))
        {
            return note.Length > maxLength ? note[..maxLength] : note;
        }

        string combined = $"{existing}\n{note}";
        return combined.Length <= maxLength
            ? combined
            : existing.Length > maxLength ? existing[..maxLength] : existing;
    }

    private static bool IsNote(string line)
    {
        string trimmed = line.Trim();
        return trimmed.StartsWith(Prefix, StringComparison.Ordinal)
            && trimmed.EndsWith(Suffix, StringComparison.Ordinal)
            && trimmed.Length >= Prefix.Length + Suffix.Length;
    }
}
