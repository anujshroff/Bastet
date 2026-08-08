using Bastet.Services.Security;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Collections.Concurrent;
using System.Reflection;

namespace Bastet.Filters;

public class GlobalSanitizationFilter(
    IInputSanitizationService sanitizationService,
    ILogger<GlobalSanitizationFilter> logger) : IAsyncActionFilter
{

    private static readonly ConcurrentDictionary<Type, PropertySanitizationInfo[]> _typeCache = new();

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {

        foreach (KeyValuePair<string, object?> argument in context.ActionArguments)
        {
            if (argument.Value != null)
            {
                SanitizeObject(argument.Value);
            }
        }

        await next();
    }

    private void SanitizeObject(object obj)
    {
        if (obj == null)
        {
            return;
        }

        Type type = obj.GetType();

        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
        {
            return;
        }

        if (obj is System.Collections.IEnumerable enumerable and not string)
        {
            foreach (object? item in enumerable)
            {
                if (item != null)
                {
                    SanitizeObject(item);
                }
            }

            return;
        }

        PropertySanitizationInfo[] properties = _typeCache.GetOrAdd(type, t => GetSanitizableProperties(t));

        foreach (PropertySanitizationInfo propInfo in properties)
        {
            try
            {
                if (propInfo.Property.GetValue(obj) is string currentValue)
                {
                    string? sanitizedValue = propInfo.Attribute.Sanitize(currentValue, sanitizationService);
                    if (sanitizedValue != currentValue)
                    {
                        propInfo.Property.SetValue(obj, sanitizedValue);

                        if (logger.IsEnabled(LogLevel.Debug))
                        {
                            logger.LogDebug(
                                "Sanitized property {PropertyName} on type {TypeName}: '{OriginalValue}' -> '{SanitizedValue}'",
                                propInfo.Property.Name,
                                type.Name,
                                TruncateForLog(currentValue),
                                TruncateForLog(sanitizedValue)
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to sanitize property {PropertyName} on type {TypeName}",
                    propInfo.Property.Name,
                    type.Name);
            }
        }

        foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {

            if (properties.Any(p => p.Property == prop))
            {
                continue;
            }

            if (prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            Type propType = prop.PropertyType;

            if (propType.IsPrimitive ||
                propType == typeof(string) ||
                propType == typeof(decimal) ||
                propType == typeof(DateTime) ||
                propType == typeof(Guid) ||
                propType.Namespace?.StartsWith("System") == true)
            {
                continue;
            }

            try
            {
                object? value = prop.GetValue(obj);
                if (value != null)
                {
                    SanitizeObject(value);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to process nested property {PropertyName} on type {TypeName}",
                    prop.Name,
                    type.Name);
            }
        }
    }

    private static string TruncateForLog(string? value)
    {
        const int maxLoggedLength = 50;

        string safe = LogSanitizer.SanitizeForLog(value);
        return safe.Length > maxLoggedLength
            ? string.Concat(safe.AsSpan(0, maxLoggedLength), "...")
            : safe;
    }

    private static PropertySanitizationInfo[] GetSanitizableProperties(Type type)
    {
        List<PropertySanitizationInfo> properties = [];

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {

            if (property.PropertyType != typeof(string) || !property.CanWrite)
            {
                continue;
            }

            if (property
                .GetCustomAttributes(typeof(SanitizationAttribute), true)
                .FirstOrDefault() is SanitizationAttribute sanitizationAttribute)
            {
                properties.Add(new PropertySanitizationInfo
                {
                    Property = property,
                    Attribute = sanitizationAttribute
                });
            }
        }

        return [.. properties];
    }

    private class PropertySanitizationInfo
    {
        public PropertyInfo Property { get; set; } = null!;
        public SanitizationAttribute Attribute { get; set; } = null!;
    }
}
