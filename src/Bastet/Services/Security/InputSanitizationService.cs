using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Bastet.Services.Security;

public partial class InputSanitizationService : IInputSanitizationService
{

    [GeneratedRegex(@"</?[A-Za-z][^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTagPattern();

    private const int MaxNameLength = 100;
    private const int MaxDescriptionLength = 1000;

    private const int MaxTagsLength = 255;

    public string StripHtml(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        string stripped = HtmlTagPattern().Replace(input, string.Empty);

        return stripped.Trim();
    }

    public string SanitizeNetworkInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        string sanitized = input.Trim();

        StringBuilder validChars = new();
        foreach (char c in sanitized)
        {
            if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == ':')
            {
                validChars.Append(c);
            }
        }

        return validChars.ToString();
    }

    public bool IsValidIpAddress(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return false;
        }

        string sanitized = SanitizeNetworkInput(ipAddress);

        if (sanitized != ipAddress.Trim())
        {
            return false;
        }

        if (!IPAddress.TryParse(sanitized, out IPAddress? parsedAddress))
        {
            return false;
        }

        return parsedAddress.ToString() == sanitized;
    }

    public string SanitizeName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        string sanitized = input.Trim();
        if (sanitized.Length > MaxNameLength)
        {
            sanitized = sanitized[..MaxNameLength];
        }

        sanitized = StripHtml(sanitized);

        return sanitized;
    }

    public string SanitizeDescription(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        string sanitized = input.Trim();
        if (sanitized.Length > MaxDescriptionLength)
        {
            sanitized = sanitized[..MaxDescriptionLength];
        }

        sanitized = StripHtml(sanitized);

        return sanitized;
    }

    public string SanitizeTags(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        string sanitized = input.Trim();

        string[] tags = [.. sanitized.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => StripHtml(tag.Trim()))
            .Where(tag => !string.IsNullOrWhiteSpace(tag) && tag.Length <= 50)
            .Take(10)];

        string joined = string.Join(",", tags);

        return joined.Length <= MaxTagsLength ? joined : TrimToWholeTags(joined);
    }

    private static string TrimToWholeTags(string joined)
    {
        string clipped = joined[..MaxTagsLength];
        int lastSeparator = clipped.LastIndexOf(',');
        return lastSeparator > 0 ? clipped[..lastSeparator] : clipped;
    }
}
