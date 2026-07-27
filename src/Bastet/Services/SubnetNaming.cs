namespace Bastet.Services;

/// <summary>
/// Composition rules for generated subnet names, shared by everything that builds one.
/// </summary>
public static class SubnetNaming
{
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
