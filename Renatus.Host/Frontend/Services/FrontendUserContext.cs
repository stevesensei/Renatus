using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Renatus.Host.Data;
using Renatus.Host.Utils;

namespace Renatus.Host.Frontend.Services;

public sealed class FrontendUserContext(AuthenticationStateProvider authenticationStateProvider)
{
    public async Task<Guid> GetUserIdAsync()
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var userIdValue = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            throw new AuthException(StatusCodes.Status401Unauthorized, "Authorization token is invalid.");
        }

        return userId;
    }
}
