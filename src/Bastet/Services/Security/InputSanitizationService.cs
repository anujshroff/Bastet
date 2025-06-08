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

        return string.Join(", ", tags);
    }
}
