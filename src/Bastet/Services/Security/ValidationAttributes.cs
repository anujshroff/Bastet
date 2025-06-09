using System.ComponentModel.DataAnnotations;

namespace Bastet.Services.Security;

/// <summary>
/// Validation attribute that sanitizes string input to prevent XSS attacks
/// </summary>
public class SanitizedStringAttribute : ValidationAttribute
{
    public bool AllowHtml { get; set; } = false;
    public bool StrictSafeText { get; set; } = false;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string stringValue)
        {
            return ValidationResult.Success;
        }

        IInputSanitizationService? sanitizationService = validationContext.GetService<IInputSanitizationService>();
        if (sanitizationService == null)
        {
            return new ValidationResult("Input sanitization service not available");
        }

        // If StrictSafeText is enabled, check if the input contains only safe characters
        if (StrictSafeText && !sanitizationService.IsSafeText(stringValue))
        {
            return new ValidationResult(ErrorMessage ?? "Input contains invalid characters");
        }

        // The actual sanitization will be handled in the controller or model binding
        // This attribute primarily serves as a marker and validator
        return ValidationResult.Success;
    }
}

/// <summary>
/// Validation attribute that ensures input contains no HTML tags
/// </summary>
public class NoHtmlAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string stringValue || string.IsNullOrWhiteSpace(stringValue))
        {
            return ValidationResult.Success;
        }

        IInputSanitizationService? sanitizationService = validationContext.GetService<IInputSanitizationService>();
        if (sanitizationService == null)
        {
            return new ValidationResult("Input sanitization service not available");
        }

        // Check if the stripped version is different from original
        string stripped = sanitizationService.StripHtml(stringValue);
        return stripped != stringValue.Trim()
            ? new ValidationResult(ErrorMessage ?? "HTML tags are not allowed in this field")
            : ValidationResult.Success;
    }
}

/// <summary>
/// Validation attribute for safe text that only allows alphanumeric and basic punctuation
/// </summary>
public class SafeTextAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string stringValue || string.IsNullOrWhiteSpace(stringValue))
        {
            return ValidationResult.Success;
        }

        IInputSanitizationService? sanitizationService = validationContext.GetService<IInputSanitizationService>();
        return sanitizationService == null
            ? new ValidationResult("Input sanitization service not available")
            : !sanitizationService.IsSafeText(stringValue)
            ? new ValidationResult(ErrorMessage ?? "Input contains invalid or potentially dangerous characters")
            : ValidationResult.Success;
    }
}

/// <summary>
/// Validation attribute for network-related inputs (IP addresses, hostnames, etc.)
/// </summary>
public class NetworkInputAttribute : ValidationAttribute
{
    public bool RequireValidIp { get; set; } = false;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string stringValue || string.IsNullOrWhiteSpace(stringValue))
        {
            return ValidationResult.Success;
        }

        IInputSanitizationService? sanitizationService = validationContext.GetService<IInputSanitizationService>();
        if (sanitizationService == null)
        {
            return new ValidationResult("Input sanitization service not available");
        }

        // If IP validation is required, validate as IP address
        if (RequireValidIp)
        {
            if (!sanitizationService.IsValidIpAddress(stringValue))
            {
                return new ValidationResult(ErrorMessage ?? "Invalid IP address format");
            }
        }
        else
        {
            // Just ensure it's safe for network input
            string sanitized = sanitizationService.SanitizeNetworkInput(stringValue);
            if (sanitized != stringValue.Trim())
            {
                return new ValidationResult(ErrorMessage ?? "Input contains invalid characters for network input");
            }
        }

        return ValidationResult.Success;
    }
}

/// <summary>
/// Validation attribute specifically for tags (comma-separated values)
/// </summary>
public class TagsAttribute : ValidationAttribute
{
    public int MaxTags { get; set; } = 10;
    public int MaxTagLength { get; set; } = 50;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string stringValue || string.IsNullOrWhiteSpace(stringValue))
        {
            return ValidationResult.Success;
        }

        IInputSanitizationService? sanitizationService = validationContext.GetService<IInputSanitizationService>();
        if (sanitizationService == null)
        {
            return new ValidationResult("Input sanitization service not available");
        }

        // Parse tags and validate
        string[] tags = [.. stringValue.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))];

        if (tags.Length > MaxTags)
        {
            return new ValidationResult($"Maximum {MaxTags} tags allowed");
        }

        foreach (string? tag in tags)
        {
            if (tag.Length > MaxTagLength)
            {
                return new ValidationResult($"Each tag must be {MaxTagLength} characters or less");
            }

            if (!sanitizationService.IsSafeText(tag))
            {
                return new ValidationResult($"Tag '{tag}' contains invalid characters");
            }
        }

        return ValidationResult.Success;
    }
}
