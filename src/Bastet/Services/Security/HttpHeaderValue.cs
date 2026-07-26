namespace Bastet.Services.Security;

/// <summary>
/// Checks whether a configured value can legally be written as an HTTP header value.
/// </summary>
/// <remarks>
/// Kestrel rejects any non-ASCII or control character (tab excepted) when it writes response headers,
/// throwing "Invalid non-ASCII or control character in header". A value that comes from configuration
/// and is written on every response therefore has to be checked once at startup: left unchecked, a
/// stray character - a CRLF from an env file edited on Windows, a curly quote pasted from a document -
/// makes every request fail, including the error pages.
/// </remarks>
public static class HttpHeaderValue
{
    private const char DeleteCharacter = (char)0x7F;

    /// <summary>
    /// True when every character in <paramref name="value"/> is permitted in a header value.
    /// A null or empty value is valid; there is nothing to write.
    /// </summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        foreach (char c in value)
        {
            // Matches what Kestrel enforces: ASCII only, and no control characters except tab.
            if (c >= DeleteCharacter || (char.IsControl(c) && c != '\t'))
            {
                return false;
            }
        }

        return true;
    }
}
