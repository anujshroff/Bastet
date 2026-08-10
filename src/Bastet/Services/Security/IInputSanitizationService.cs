namespace Bastet.Services.Security;

public interface IInputSanitizationService
{

    string StripHtml(string? input);

    bool IsSafeText(string? input);

    string SanitizeNetworkInput(string? input);

    bool IsValidIpAddress(string? ipAddress);

    string SanitizeName(string? input);

    string SanitizeDescription(string? input);

    string SanitizeTags(string? input);
}
