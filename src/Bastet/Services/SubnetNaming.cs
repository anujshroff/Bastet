using System.Text.RegularExpressions;

namespace Bastet.Services;

/// <summary>
/// Composition rules for generated subnet names, shared by everything that builds one.
/// </summary>
public static partial class SubnetNaming
{
    /// <summary>
    /// Every character outside the SafeText class that <c>CreateSubnetViewModel.Name</c> is
    /// validated against.
    /// </summary>
    /// <remarks>
    /// This must stay the complement of <c>InputSanitizationService</c>'s <c>SafeTextPattern</c>,
    /// which is the source of truth; <c>SubnetNamingSafeTextTests</c> pins the two in agreement
    /// character by character so they cannot drift apart silently.
    /// </remarks>
    [GeneratedRegex(@"[^a-zA-Z0-9\s\-_.,!?@#$%&()+=]", RegexOptions.Compiled)]
    private static partial Regex OutsideSafeText();

    /// <summary>
    /// Drops characters a generated name may not contain, so a name this class composes is one the
    /// create form will actually accept.
    /// </summary>
    /// <remarks>
    /// Stored subnet names are deliberately not restricted to the SafeText class - Edit applies only
    /// [NoHtml] and [SanitizeName] - so a parent can legitimately be called "Prod/Web". Copying that
    /// straight into a prefilled child name produced a value the very next POST rejected with
    /// "Subnet name contains invalid characters", on a flow the Details page's own button drives.
    ///
    /// Filtering rather than rejecting keeps as much of the parent's name as is usable: discarding
    /// the whole name for one bad character gives a worse default than "ProdWeb-10.0.0.0-17". The
    /// result is trimmed because the class admits whitespace, so a name like "/ / /" would otherwise
    /// survive as spaces and compose a leading-blank name.
    /// </remarks>
    public static string ToSafeText(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : OutsideSafeText().Replace(value, string.Empty).Trim();

    /// <summary>
    /// Appends <paramref name="suffix"/> to <paramref name="baseName"/> within
    /// <paramref name="maxLength"/> by shortening the base name.
    /// </summary>
    /// <remarks>
    /// The suffix is what makes a generated name distinguishable - a prefix, a CIDR, a
    /// disambiguating counter - so the base name gives way, never the suffix. Truncating the
    /// combined string instead would cut the suffix straight back off for a base name already at
    /// the limit, handing back the very name the caller was trying to make distinct.
    /// </remarks>
    public static string WithSuffix(string? baseName, string suffix, int maxLength)
    {
        suffix ??= string.Empty;
        baseName ??= string.Empty;

        int room = maxLength - suffix.Length;
        string trimmedBase = room <= 0
            ? string.Empty
            : baseName.Length > room ? baseName[..room] : baseName;

        string combined = trimmedBase + suffix;

        // A suffix longer than the limit on its own is the only way to get here over-length.
        return combined.Length > maxLength ? combined[..maxLength] : combined;
    }
}
