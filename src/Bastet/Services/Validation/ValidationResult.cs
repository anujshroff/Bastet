namespace Bastet.Services.Validation;

public class ValidationResult
{

    public bool IsValid => Errors.Count == 0;

    public List<ValidationError> Errors { get; } = [];

    public void AddError(string code, string message) =>
        Errors.Add(new ValidationError(code, message));

}

public class ValidationError(string code, string message)
{

    public string Code { get; } = code;

    public string Message { get; } = message;
}
