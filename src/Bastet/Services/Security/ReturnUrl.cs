namespace Bastet.Services.Security;

public static class ReturnUrl
{
    public static bool IsLocal(string? url)
    {
        if (string.IsNullOrEmpty(url) || !HttpHeaderValue.IsValid(url) || url.Any(char.IsControl))
        {
            return false;
        }

        if (url[0] == '/')
        {
            return url.Length == 1 || (url[1] != '/' && url[1] != '\\');
        }

        if (url[0] == '~' && url.Length > 1 && url[1] == '/')
        {
            return url.Length == 2 || (url[2] != '/' && url[2] != '\\');
        }

        return false;
    }
}
