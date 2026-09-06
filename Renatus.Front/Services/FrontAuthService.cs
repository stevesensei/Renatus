using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Renatus.Front.Models;

namespace Renatus.Front.Services;

public sealed class FrontAuthService
{
    private readonly ApiClient _apiClient;
    private readonly FrontSession _session;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FrontAuthService(ApiClient apiClient, FrontSession session, IHttpContextAccessor httpContextAccessor)
    {
        _apiClient = apiClient;
        _session = session;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<AuthUserDto> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.PostAsync<AuthResponse>("/api/auth/login", request, cancellationToken);
        await SignInAsync(response);
        return response.User;
    }

    public async Task<AuthUserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.PostAsync<AuthResponse>("/api/auth/register", request, cancellationToken);
        await SignInAsync(response);
        return response.User;
    }

    public async Task SignOutAsync()
    {
        _session.SignOut();
        var context = _httpContextAccessor.HttpContext;
        if (context is not null)
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    private async Task SignInAsync(AuthResponse response)
    {
        _session.SignIn(response.User, response.AccessToken);

        var context = _httpContextAccessor.HttpContext;
        if (context is null)
        {
            return;
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, response.User.Id.ToString()),
            new Claim(ClaimTypes.Name, response.User.Username),
            new Claim(ClaimTypes.Role, response.User.Role.ToString()),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        // The backend JWT is stored only in the encrypted auth cookie, never in
        // browser storage or page markup. It is not added to the cookie claims.
        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = response.ExpiresAt.UtcDateTime,
        };
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = "access_token", Value = response.AccessToken }
        ]);

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            properties);
    }
}
