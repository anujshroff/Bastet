using System.Text.RegularExpressions;

namespace Bastet.Tests;

internal static partial class SafeTextOracle
{
    [GeneratedRegex(@"^[a-zA-Z0-9\s\-_.,!?@#$%&()+=]*$", RegexOptions.Compiled)]
    private static partial Regex SafeTextPattern();

    public static bool IsSafe(string? input) =>
        string.IsNullOrWhiteSpace(input) || SafeTextPattern().IsMatch(input);
}
