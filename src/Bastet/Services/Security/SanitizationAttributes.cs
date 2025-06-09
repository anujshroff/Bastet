namespace Bastet.Services.Security;

/// <summary>
/// Base attribute for sanitization markers
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public abstract class SanitizationAttribute : Attribute
{
    /// <summary>
    /// Apply sanitization to the value using the provided service
    /// </summary>
    public abstract string? Sanitize(string? value, IInputSanitizationService sanitizationService);
}

/// <summary>
/// Sanitizes name fields (max 100 chars, strips HTML)
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class SanitizeNameAttribute : SanitizationAttribute
{
    public override string? Sanitize(string? value, IInputSanitizationService sanitizationService) => sanitizationService.SanitizeName(value);
}

/// <summary>
/// Sanitizes description fields (max 1000 chars, strips HTML)
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class SanitizeDescriptionAttribute : SanitizationAttribute
{
    public override string? Sanitize(string? value, IInputSanitizationService sanitizationService) => sanitizationService.SanitizeDescription(value);
}

/// <summary>
/// Sanitizes network input fields (IP addresses, hostnames)
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class SanitizeNetworkInputAttribute : SanitizationAttribute
{
    public override string? Sanitize(string? value, IInputSanitizationService sanitizationService) => sanitizationService.SanitizeNetworkInput(value);
}

/// <summary>
/// Sanitizes tag fields (comma-separated values)
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class SanitizeTagsAttribute : SanitizationAttribute
{
    public override string? Sanitize(string? value, IInputSanitizationService sanitizationService) => sanitizationService.SanitizeTags(value);
}

/// <summary>
/// General string sanitization (strips HTML, encodes entities)
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class SanitizeGeneralAttribute : SanitizationAttribute
{
    public bool AllowHtml { get; set; } = false;

    public override string? Sanitize(string? value, IInputSanitizationService sanitizationService) => sanitizationService.SanitizeString(value, AllowHtml);
}
