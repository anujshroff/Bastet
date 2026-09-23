namespace Bastet.Services;

public static class SubnetNaming
{

    public static string WithSuffix(string? baseName, string suffix, int maxLength)
    {
        suffix ??= string.Empty;
        baseName ??= string.Empty;

        int room = maxLength - suffix.Length;
        string trimmedBase = room <= 0
            ? string.Empty
            : baseName.Length > room ? baseName[..room] : baseName;

        if (trimmedBase.Length > 0 && char.IsHighSurrogate(trimmedBase[^1]))
        {
            trimmedBase = trimmedBase[..^1];
        }

        string combined = trimmedBase + suffix;

        return combined.Length > maxLength ? combined[..maxLength] : combined;
    }
}
