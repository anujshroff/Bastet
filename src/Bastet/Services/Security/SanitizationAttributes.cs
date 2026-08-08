namespace Bastet.Services.Security;

[AttributeUsage(AttributeTargets.Property)]
public abstract class SanitizationAttribute : Attribute
{

    public abstract string? Sanitize(string? value, IInputSanitizationService sanitizationService);
}

[AttributeUsage(AttributeTargets.Property)]
public class SanitizeNameAttribute : SanitizationAttribute
{
    public override string? Sanitize(string? value, IInputSanitizationService sanitizationService) => sanitizationService.SanitizeName(value);
}

[AttributeUsage(AttributeTargets.Property)]
public class SanitizeDescriptionAttribute : SanitizationAttribute
{
    public override string? Sanitize(string? value, IInputSanitizationService sanitizationService) => sanitizationService.SanitizeDescription(value);
}

[AttributeUsage(AttributeTargets.Property)]
public class SanitizeNetworkInputAttribute : SanitizationAttribute
{
    public override string? Sanitize(string? value, IInputSanitizationService sanitizationService) => sanitizationService.SanitizeNetworkInput(value);
}

[AttributeUsage(AttributeTargets.Property)]
public class SanitizeTagsAttribute : SanitizationAttribute
{
    public override string? Sanitize(string? value, IInputSanitizationService sanitizationService) => sanitizationService.SanitizeTags(value);
}
