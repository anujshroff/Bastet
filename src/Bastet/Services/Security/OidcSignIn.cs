using Microsoft.AspNetCore.Authentication;

namespace Bastet.Services.Security;

public static class OidcSignIn
{
    public static Task OnTicketReceived(TicketReceivedContext context)
    {
        AuthenticationProperties? properties = context.Properties;
        properties?.StoreTokens(
            [.. properties.GetTokens().Where(token => token.Name == "id_token")]);

        if (!ReturnUrl.IsLocal(context.ReturnUri))
        {
            context.ReturnUri = "/";
        }

        return Task.CompletedTask;
    }
}
