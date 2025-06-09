namespace Bastet.Services.Security;

/// <summary>
/// Service for sanitizing user input to prevent XSS and injection attacks
/// </summary>
public interface IInputSanitizationService
{
    /// <summary>
    /// Sanitizes a string by removing or encoding potentially dangerous content
    /// </summary>
    /// <param name="input">The input string to sanitize</param>
    /// <param name="allowHtml">Whether to allow basic HTML tags (default: false)</param>
    /// <returns>The sanitized string</returns>
    string SanitizeString(string? input, bool allowHtml = false);

    /// <summary>
    /// Removes all HTML tags from the input string
    /// </summary>
    /// <param name="input">The input string to strip HTML from</param>
    /// <returns>The string with HTML tags removed</returns>
    string StripHtml(string? input);

    /// <summary>
    /// Encodes HTML entities in the input string
    /// </summary>
    /// <param name="input">The input string to encode</param>
    /// <returns>The HTML-encoded string</returns>
    string EncodeHtml(string? input);

    /// <summary>
    /// Validates that a string contains only safe characters (alphanumeric, spaces, and basic punctuation)
    /// </summary>
    /// <param name="input">The input string to validate</param>
    /// <returns>True if the string is safe, false otherwise</returns>
    bool IsSafeText(string? input);

    /// <summary>
    /// Sanitizes input for use in network-related fields (IP addresses, domain names, etc.)
    /// </summary>
    /// <param name="input">The input string to sanitize</param>
    /// <returns>The sanitized network input</returns>
    string SanitizeNetworkInput(string? input);

    /// <summary>
    /// Validates that an IP address string is properly formatted and safe
    /// </summary>
    /// <param name="ipAddress">The IP address string to validate</param>
    /// <returns>True if the IP address is valid and safe, false otherwise</returns>
    bool IsValidIpAddress(string? ipAddress);

    /// <summary>
    /// Sanitizes a name field (stricter validation for names)
    /// </summary>
    /// <param name="input">The input string to sanitize</param>
    /// <returns>The sanitized name</returns>
    string SanitizeName(string? input);

    /// <summary>
    /// Sanitizes a description field (allows more content but removes dangerous elements)
    /// </summary>
    /// <param name="input">The input string to sanitize</param>
    /// <returns>The sanitized description</returns>
    string SanitizeDescription(string? input);

    /// <summary>
    /// Sanitizes tags field (comma-separated values)
    /// </summary>
    /// <param name="input">The input string to sanitize</param>
    /// <returns>The sanitized tags</returns>
    string SanitizeTags(string? input);
}
