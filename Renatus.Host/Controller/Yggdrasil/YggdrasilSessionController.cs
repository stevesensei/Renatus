using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service;
using Renatus.Host.Utils;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Validation.AspNetCore;
using Renatus.Host.Service.Connect;

namespace Renatus.Host.Controller.Yggdrasil;

[Route("api/yggdrasil/sessionserver/session/minecraft")]
public sealed class YggdrasilSessionController(
    YggdrasilService yggdrasilService,
    ConnectSessionService connectSessionService,
    IAuthenticationSchemeProvider schemeProvider)
    : YggdrasilControllerBase
{
    [HttpPost("join")]
    public async Task<IActionResult> Join(JoinRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (Request.Headers.Authorization.Count > 0)
            {
                if (await schemeProvider.GetSchemeAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme) is null)
                    throw YggdrasilException.InvalidToken();
                var result = await HttpContext.AuthenticateAsync(
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                if (!result.Succeeded || result.Principal is null) throw YggdrasilException.InvalidToken();
                await connectSessionService.JoinAsync(result.Principal, request, cancellationToken);
            }
            else if (await schemeProvider.GetSchemeAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme) is not null
                && !string.IsNullOrWhiteSpace(request.AccessToken))
            {
                Request.Headers.Authorization = $"Bearer {request.AccessToken}";
                var result = await HttpContext.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                if (result.Succeeded && result.Principal is not null)
                    await connectSessionService.JoinAsync(result.Principal, request, cancellationToken);
                else await yggdrasilService.JoinAsync(request, cancellationToken);
            }
            else await yggdrasilService.JoinAsync(request, cancellationToken);
            return NoContent();
        }
        catch (YggdrasilException exception)
        {
            return YggdrasilError(exception);
        }
    }

    [HttpGet("hasJoined")]
    public async Task<ActionResult<SessionProfileResponse>> HasJoined(
        [FromQuery] string username,
        [FromQuery] string serverId,
        CancellationToken cancellationToken)
    {
        var response = await connectSessionService.HasJoinedAsync(username, serverId, cancellationToken)
            ?? await yggdrasilService.HasJoinedAsync(username, serverId, cancellationToken);
        return response is null ? NoContent() : Ok(response);
    }

    [HttpGet("profile/{id}")]
    public async Task<ActionResult<SessionProfileResponse>> Profile(
        string id,
        [FromQuery] string? unsigned,
        CancellationToken cancellationToken)
    {
        try
        {
            var playerId = YggdrasilFormat.ParseUnsignedUuid(id);
            var response = await yggdrasilService.GetSessionProfileAsync(playerId, unsigned != "false", cancellationToken);
            return response is null ? NoContent() : Ok(response);
        }
        catch (YggdrasilException exception)
        {
            return YggdrasilError(exception);
        }
    }
}
