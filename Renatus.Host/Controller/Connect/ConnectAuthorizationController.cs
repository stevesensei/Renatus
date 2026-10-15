using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Renatus.Host.Service.Connect;

namespace Renatus.Host.Controller.Connect;

[ApiController]
public sealed class ConnectAuthorizationController(ConnectProtocolService protocolService) : ControllerBase
{
    /// <summary>Starts a server-held consent interaction or consumes its single-use authenticated decision.</summary>
    [AcceptVerbs("GET", "POST")]
    [Route("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest();
        if (request?.ClientId is null) return NotFound();
        try
        {
            ConnectProtocolService.ValidateScopes(request.GetScopes());
            var interactionId = request.GetParameter(ConnectConstants.InteractionParameter).ToString();
            if (string.IsNullOrWhiteSpace(interactionId))
            {
                var completionUrl = CreateCompletionUrl(request);
                var interaction = await protocolService.CreateInteractionAsync(
                    request.ClientId, request.GetScopes(), false, completionUrl, cancellationToken);
                return Redirect($"/connect/consent?interaction={Uri.EscapeDataString(interaction.InteractionId)}");
            }

            var completed = await protocolService.ConsumeCompletedAsync(interactionId);
            if (completed is null || !MatchesInteraction(completed, request, false))
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            if (!completed.Approved) return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var principal = await protocolService.CreatePrincipalAsync(completed.UserId,
                completed.PlayerMinecraftUuid, request.GetScopes(), request.ClientId, cancellationToken);
            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
        catch (ConnectException)
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
    }

    /// <summary>Handles code, rotating refresh-token and device-code exchanges after current account state is revalidated.</summary>
    [HttpPost("~/connect/token")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Exchange(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest();
        if (request is null) return NotFound();
        if (!request.IsAuthorizationCodeGrantType()
            && !request.IsRefreshTokenGrantType()
            && !request.IsDeviceCodeGrantType())
            return BadRequest();
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal is null)
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var clientId = request.ClientId
            ?? result.Principal.GetClaim(OpenIddictConstants.Claims.ClientId);
        if (string.IsNullOrWhiteSpace(clientId))
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        try
        {
            var principal = await protocolService.RefreshPrincipalAsync(result.Principal, clientId, cancellationToken);
            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
        catch (ConnectException)
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
    }

    /// <summary>Turns a valid OpenIddict user code into the same opaque, frontend-authenticated consent interaction.</summary>
    [HttpGet("~/connect/verify")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Verify(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest();
        if (request is null) return NotFound();
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal is null)
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var clientId = result.Principal.GetClaim(OpenIddictConstants.Claims.ClientId);
        if (string.IsNullOrWhiteSpace(clientId))
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var interactionId = request.GetParameter(ConnectConstants.InteractionParameter).ToString();
        if (string.IsNullOrWhiteSpace(interactionId))
        {
            var interaction = await protocolService.CreateInteractionAsync(clientId,
                result.Principal.GetScopes(), true, CreateCompletionUrl(request), cancellationToken);
            return Redirect($"/connect/device/verify?interaction={Uri.EscapeDataString(interaction.InteractionId)}");
        }
        var completed = await protocolService.ConsumeCompletedAsync(interactionId);
        if (completed is null || !completed.Approved || !MatchesInteraction(completed, request, true))
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        try
        {
            var principal = await protocolService.CreatePrincipalAsync(completed.UserId,
                completed.PlayerMinecraftUuid, result.Principal.GetScopes(), clientId, cancellationToken);
            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
        catch (ConnectException)
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
    }

    /// <summary>Binds a single-use decision to the original client, scopes and full protocol request, preventing parameter substitution after consent.</summary>
    private bool MatchesInteraction(ConnectCompletedInteraction completed, OpenIddictRequest request, bool deviceFlow)
        => completed.Pending.DeviceFlow == deviceFlow
            && string.Equals(completed.Pending.CompletionUrl,
                CreateCompletionUrl(request).Replace("__INTERACTION__",
                    Uri.EscapeDataString(completed.Pending.InteractionId), StringComparison.Ordinal),
                StringComparison.Ordinal);

    private string CreateCompletionUrl(OpenIddictRequest request)
    {
        var parameters = request.GetParameters()
            .Where(parameter => parameter.Key != ConnectConstants.InteractionParameter)
            .ToDictionary(parameter => parameter.Key, parameter => parameter.Value.ToString());
        parameters[ConnectConstants.InteractionParameter] = "__INTERACTION__";
        var path = Request.Path.Value ?? "/connect/authorize";
        return QueryHelpers.AddQueryString(path, parameters);
    }
}
