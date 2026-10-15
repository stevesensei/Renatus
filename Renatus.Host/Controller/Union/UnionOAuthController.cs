using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service.Union;
using Renatus.Host.Utils;

namespace Renatus.Host.Controller.Union;

[ApiController]
public sealed class UnionOAuthController(UnionOAuthService oauthService) : ControllerBase
{
    /// <summary>Public discovery returns the Union web OAuth signature public key and never private material.</summary>
    [AllowAnonymous]
    [HttpGet("api/union/member/oauth2")]
    public async Task<IActionResult> PublicKey(CancellationToken cancellationToken)
    {
        Response.Headers.AccessControlAllowOrigin = "*";
        try
        {
            return Ok(await oauthService.GetPublicKeyAsync(cancellationToken));
        }
        catch (UnionException exception)
        {
            return StatusCode(exception.StatusCode, new { error = exception.Message });
        }
    }

    /// <summary>Legacy center entry stores its continuation and redirects to local Vue consent without placing a Bearer token in the URL.</summary>
    [AllowAnonymous]
    [HttpGet("api/union/member/oauth2/grant")]
    public async Task<IActionResult> Grant(CancellationToken cancellationToken)
    {
        try
        {
            return Redirect(await oauthService.CreateInteractionAsync(Request.Query, cancellationToken));
        }
        catch (UnionException exception)
        {
            return StatusCode(exception.StatusCode, new { error = exception.Message });
        }
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/union/oauth2/interactions/{interactionId:guid}")]
    public async Task<GlobalResponse<UnionOAuthInteractionResponse>> Interaction(
        Guid interactionId, CancellationToken cancellationToken)
    {
        try
        {
            return GlobalResponse<UnionOAuthInteractionResponse>.Success(
                await oauthService.GetInteractionAsync(interactionId, cancellationToken));
        }
        catch (UnionException exception)
        {
            return GlobalResponse<UnionOAuthInteractionResponse>.ResultError(message: exception.Message);
        }
    }

    /// <summary>Authenticated approval or denial consumes the interaction once and returns only a trusted center continuation URL.</summary>
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/union/oauth2/interactions/{interactionId:guid}")]
    public async Task<GlobalResponse<UnionOAuthCompleteResponse>> Complete(
        Guid interactionId,
        UnionOAuthCompleteRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return GlobalResponse<UnionOAuthCompleteResponse>.ResultError(message: "Authorization token is invalid.");
        }
        try
        {
            return GlobalResponse<UnionOAuthCompleteResponse>.Success(
                await oauthService.CompleteInteractionAsync(userId, interactionId, request, cancellationToken));
        }
        catch (UnionException exception)
        {
            return GlobalResponse<UnionOAuthCompleteResponse>.ResultError(message: exception.Message);
        }
    }
}
