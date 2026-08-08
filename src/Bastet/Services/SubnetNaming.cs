using System.Text.RegularExpressions;

namespace Bastet.Services;

public static partial class SubnetNaming
{

    [GeneratedRegex(@"[^a-zA-Z0-9\s\-_.,!?@#$%&()+=]", RegexOptions.Compiled)]
    private static partial Regex OutsideSafeText();

    public static string ToSafeText(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : OutsideSafeText().Replace(value, string.Empty).Trim();

    public static string WithSuffix(string? baseName, string suffix, int maxLength)
    {
        suffix ??= string.Empty;
        baseName ??= string.Empty;

        int room = maxLength - suffix.Length;
        string trimmedBase = room <= 0
            ? string.Empty
            : baseName.Length > room ? baseName[..room] : baseName;

        string combined = trimmedBase + suffix;

        return combined.Length > maxLength ? combined[..maxLength] : combined;
    }
}
