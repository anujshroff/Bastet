using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace Bastet.Services.Security;

/// <summary>
/// Implementation of input sanitization service to prevent XSS and injection attacks
/// </summary>
public partial class InputSanitizationService : IInputSanitizationService
{
    // Regex patterns for validation
    [GeneratedRegex(@"^[a-zA-Z0-9\s\-_.,!?@#$%&()+=]*$", RegexOptions.Compiled)]
    private static partial Regex SafeTextPattern();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"javascript:|vbscript:|onload|onerror|onclick|onmouseover|onkeydown|onkeyup|onchange|onsubmit|data:", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex DangerousScriptPattern();

    [GeneratedRegex(@"^[a-zA-Z0-9\.\-_:]*$", RegexOptions.Compiled)]
    private static partial Regex NetworkInputPattern();

    // Maximum lengths for different input types
    private const int MaxStringLength = 500;
    private const int MaxNameLength = 100;
    private const int MaxDescriptionLength = 1000;

    /// <summary>
    /// Width of the Tags column, and the ceiling this service's own output must respect.
    /// </summary>
    private const int MaxTagsLength = 255;

    public string SanitizeString(string? input, bool allowHtml = false)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        // Trim and limit length
        string sanitized = input.Trim();
        if (sanitized.Length > MaxStringLength)
        {
            sanitized = sanitized[..MaxStringLength];
        }

        // Remove or encode HTML based on allowHtml parameter
        if (allowHtml)
        {
            // If HTML is allowed, only remove dangerous scripts but keep basic HTML
            sanitized = RemoveDangerousScripts(sanitized);
            // Still encode the result for safety
            sanitized = EncodeHtml(sanitized);
        }
        else
        {
            // Remove all HTML tags first, then encode
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

        // Remove HTML tags completely
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

        // Check for safe characters only
        return SafeTextPattern().IsMatch(input);
    }

    public string SanitizeNetworkInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        string sanitized = input.Trim();

        // Remove invalid characters for network inputs
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

        // First, sanitize the input
        string sanitized = SanitizeNetworkInput(ipAddress);

        // Check if sanitization changed the input (meaning there were invalid characters)
        if (sanitized != ipAddress.Trim())
        {
            return false;
        }

        // Try to parse as IP address
        if (!IPAddress.TryParse(sanitized, out IPAddress? parsedAddress))
        {
            return false;
        }

        // Additional validation - ensure the original input exactly matches the parsed result
        return parsedAddress.ToString() == sanitized;
    }

    private static string RemoveDangerousScripts(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        // Remove dangerous script patterns
        return DangerousScriptPattern().Replace(input, string.Empty);
    }

    /// <summary>
    /// Sanitizes a name field (stricter validation for names)
    /// </summary>
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

        // Remove HTML tags completely (don't encode here)
        sanitized = StripHtml(sanitized);

        return sanitized;
    }

    /// <summary>
    /// Sanitizes a description field (allows more content but removes dangerous elements)
    /// </summary>
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

        // Remove HTML tags completely
        sanitized = StripHtml(sanitized);

        return sanitized;
    }

    /// <summary>
    /// Sanitizes tags field (comma-separated values)
    /// </summary>
    public string SanitizeTags(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        string sanitized = input.Trim();

        // Split tags, sanitize each one, and rejoin
        string[] tags = [.. sanitized.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => StripHtml(tag.Trim()))
            .Where(tag => !string.IsNullOrWhiteSpace(tag) && tag.Length <= 50)
            .Take(10)];

        // Joined with a bare comma, not ", ". Sanitization runs in an action filter, which MVC
        // executes *after* model validation - so [StringLength(255)] has already passed by the time
        // this rewrites the value, and any separator wider than the one it replaces makes the result
        // longer than the value that was validated. Ten tags gained nine characters that way, enough
        // to push a legal 249-character value past the 255-wide column and fail the insert with a
        // generic error naming nothing. Every other step here only removes, so a single-character
        // separator makes the whole method non-expanding by construction.
        string joined = string.Join(",", tags);

        // Belt and braces. Unreachable while the input respected its own length limit, but this
        // method's output lands directly in a fixed-width column and should not depend on a caller
        // elsewhere having validated first.
        return joined.Length <= MaxTagsLength ? joined : TrimToWholeTags(joined);
    }

    /// <summary>
    /// Cuts an over-long tag list back to <see cref="MaxTagsLength"/> on a tag boundary, so the
    /// result never ends in a half-written tag.
    /// </summary>
    private static string TrimToWholeTags(string joined)
    {
        string clipped = joined[..MaxTagsLength];
        int lastSeparator = clipped.LastIndexOf(',');
        return lastSeparator > 0 ? clipped[..lastSeparator] : clipped;
    }
}
