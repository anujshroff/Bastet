namespace Bastet.Services.Validation;

/// <summary>
/// Represents the result of a validation operation
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Gets whether the validation passed
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Gets the list of validation errors
    /// </summary>
    public List<ValidationError> Errors { get; } = [];

    /// <summary>
    /// Adds an error to the validation result
    /// </summary>
    public void AddError(string code, string message) =>
        Errors.Add(new ValidationError(code, message));

    /// <summary>
    /// Creates a new successful validation result
    /// </summary>
    public static ValidationResult Success() => new();

    /// <summary>
    /// Creates a new validation result with a single error
    /// </summary>
    public static ValidationResult Error(string code, string message)
    {
        ValidationResult result = new();
        result.AddError(code, message);
        return result;
    }
}

/// <summary>
/// Represents a validation error
/// </summary>
/// <remarks>
/// Creates a new validation error
/// </remarks>
public class ValidationError(string code, string message)
{
    /// <summary>
    /// Gets the error code
    /// </summary>
    public string Code { get; } = code;

    /// <summary>
    /// Gets the error message
    /// </summary>
    public string Message { get; } = message;
}
