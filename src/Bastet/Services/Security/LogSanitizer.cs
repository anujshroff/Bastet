namespace Bastet.Services.Security;

public static class LogSanitizer
{

    private const char Tab = '\t';

    public static string SanitizeForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return !value.Any(c => char.IsControl(c) && c != Tab)
            ? value
            : string.Concat(value.Where(c => !char.IsControl(c) || c == Tab));
    }
}
