using System.ComponentModel.DataAnnotations;

namespace Bastet.Services.Security;

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

        string stripped = sanitizationService.StripHtml(stringValue);
        return stripped != stringValue.Trim()
            ? new ValidationResult(ErrorMessage ?? "HTML tags are not allowed in this field")
            : ValidationResult.Success;
    }
}

public class NetworkInputAttribute : ValidationAttribute
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

        return !sanitizationService.IsValidIpAddress(stringValue)
            ? new ValidationResult(ErrorMessage ?? "Invalid IP address format")
            : ValidationResult.Success;
    }
}

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
        }

        return ValidationResult.Success;
    }
}
