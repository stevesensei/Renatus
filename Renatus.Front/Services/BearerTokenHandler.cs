using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Renatus.Front.Services;

public sealed class BearerTokenHandler(
    FrontSession session,
    IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var accessToken = session.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            accessToken = await ReadCookieTokenAsync();
        }

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string?> ReadCookieTokenAsync()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return null;
        }

        var result = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!result.Succeeded)
        {
            return null;
        }

        var accessToken = result.Properties.GetTokenValue("access_token");
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            session.SetAccessToken(accessToken);
        }

        return accessToken;
    }
}
