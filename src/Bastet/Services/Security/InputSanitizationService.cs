using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace Bastet.Services.Security;

public partial class InputSanitizationService : IInputSanitizationService
{

    [GeneratedRegex(@"^[a-zA-Z0-9\s\-_.,!?@#$%&()+=]*$", RegexOptions.Compiled)]
    private static partial Regex SafeTextPattern();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"javascript:|vbscript:|onload|onerror|onclick|onmouseover|onkeydown|onkeyup|onchange|onsubmit|data:", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex DangerousScriptPattern();

    private const int MaxStringLength = 500;
    private const int MaxNameLength = 100;
    private const int MaxDescriptionLength = 1000;

    private const int MaxTagsLength = 255;

    public string SanitizeString(string? input, bool allowHtml = false)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        string sanitized = input.Trim();
        if (sanitized.Length > MaxStringLength)
        {
            sanitized = sanitized[..MaxStringLength];
        }

        if (allowHtml)
        {

            sanitized = RemoveDangerousScripts(sanitized);

            sanitized = EncodeHtml(sanitized);
        }
        else
        {

            sanitized = StripHtml(sanitized);
            sanitized = EncodeHtml(sanitized);
        }

        return sanitized;
    }

    public string StripHtml(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        string stripped = HtmlTagPattern().Replace(input, string.Empty);

        return stripped.Trim();
    }

    public string EncodeHtml(string? input) => string.IsNullOrWhiteSpace(input) ? string.Empty : HttpUtility.HtmlEncode(input);

    public bool IsSafeText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        return SafeTextPattern().IsMatch(input);
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

    private static string RemoveDangerousScripts(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        return DangerousScriptPattern().Replace(input, string.Empty);
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
