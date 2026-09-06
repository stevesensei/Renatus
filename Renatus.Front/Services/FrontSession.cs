using Renatus.Front.Models;

namespace Renatus.Front.Services;

public sealed class FrontSession
{
    public AuthUserDto? CurrentUser { get; private set; }

    public bool IsAuthenticated => CurrentUser is not null;

    internal string? AccessToken { get; private set; }

    public void SignIn(AuthUserDto user, string accessToken)
    {
        CurrentUser = user;
        AccessToken = accessToken;
    }

    internal void SetAccessToken(string accessToken) => AccessToken = accessToken;

    public void SignOut()
    {
        CurrentUser = null;
        AccessToken = null;
    }
}
