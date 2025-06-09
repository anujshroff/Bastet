using Bastet.Services.Security;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Collections.Concurrent;
using System.Reflection;

namespace Bastet.Filters;

/// <summary>
/// Global action filter that automatically sanitizes input based on property attributes
/// </summary>
public class GlobalSanitizationFilter(
    IInputSanitizationService sanitizationService,
    ILogger<GlobalSanitizationFilter> logger) : IAsyncActionFilter
{

    // Cache for reflection results to improve performance
    private static readonly ConcurrentDictionary<Type, PropertySanitizationInfo[]> _typeCache = new();

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Process each action argument
        foreach (KeyValuePair<string, object?> argument in context.ActionArguments)
        {
            if (argument.Value != null)
            {
                SanitizeObject(argument.Value);
            }
        }

        // Continue with the action execution
        await next();
    }

    private void SanitizeObject(object obj)
    {
        if (obj == null)
        {
            return;
        }

        Type type = obj.GetType();

        // Skip primitive types and strings (handled by property sanitization)
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
        {
            return;
        }

        // Handle collections
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

        // Get or create cached property info for this type
        PropertySanitizationInfo[] properties = _typeCache.GetOrAdd(type, t => GetSanitizableProperties(t));

        // Apply sanitization to each property
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
                        logger.LogDebug(
                            "Sanitized property {PropertyName} on type {TypeName}: '{OriginalValue}' -> '{SanitizedValue}'",
                            propInfo.Property.Name,
                            type.Name,
                            currentValue.Length > 50 ? string.Concat(currentValue.AsSpan(0, 50), "...") : currentValue,
                            sanitizedValue?.Length > 50 ? string.Concat(sanitizedValue.AsSpan(0, 50), "...") : sanitizedValue
                        );
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

        // Recursively sanitize nested objects
        foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Skip properties with sanitization attributes (already handled)
            if (properties.Any(p => p.Property == prop))
            {
                continue;
            }

            // Skip indexers
            if (prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            Type propType = prop.PropertyType;

            // Skip primitive types, strings, and system types
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

    private static PropertySanitizationInfo[] GetSanitizableProperties(Type type)
    {
        List<PropertySanitizationInfo> properties = [];

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Property must be a string and have a setter
            if (property.PropertyType != typeof(string) || !property.CanWrite)
            {
                continue;
            }

            // Check for sanitization attributes

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
