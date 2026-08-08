namespace Bastet.Services;

public static class FullyAllocatedNote
{
    private const string Prefix = "Fully allocated by Azure subnet '";
    private const string Suffix = "' which encompasses the entire address space.";

    public static string For(string? azureSubnetName) =>
        $"{Prefix}{Normalise(azureSubnetName)}{Suffix}";

    private static string? Normalise(string? azureSubnetName) =>
        azureSubnetName?.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

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
