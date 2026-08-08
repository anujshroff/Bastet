namespace Bastet.Services.Security;

public static class HttpHeaderValue
{
    private const char DeleteCharacter = (char)0x7F;

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        foreach (char c in value)
        {

            if (c >= DeleteCharacter || (char.IsControl(c) && c != '\t'))
            {
                return false;
            }
        }

        return true;
    }
}
