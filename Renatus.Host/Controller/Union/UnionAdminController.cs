using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service.Union;
using Renatus.Host.Utils;

namespace Renatus.Host.Controller.Union;

[ApiController]
[Route("api/admin/union/member")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public sealed class UnionAdminController(UnionService unionService) : ControllerBase
{
    /// <summary>Frontend Admin Bearer JWT only; returns safe sync/key versions without secrets or upstream payloads.</summary>
    [HttpGet("status")]
    public async Task<GlobalResponse<UnionAdminStatusResponse>> Status(CancellationToken cancellationToken)
    {
        return GlobalResponse<UnionAdminStatusResponse>.Success(
            await unionService.GetStatusAsync(cancellationToken));
    }

    /// <summary>Exposes a hasMemberKey indicator; persisted member and signing keys remain write-only.</summary>
    [HttpGet("settings")]
    public async Task<GlobalResponse<UnionAdminSettingsResponse>> Settings(CancellationToken cancellationToken)
    {
        return GlobalResponse<UnionAdminSettingsResponse>.Success(
            await unionService.GetSettingsAsync(cancellationToken));
    }

    /// <summary>Updates bounded settings, retaining an omitted secret; frontend business failures remain HTTP 200 GlobalResponse envelopes.</summary>
    [HttpPut("settings")]
    public async Task<GlobalResponse<UnionAdminSettingsResponse>> UpdateSettings(
        UnionAdminUpdateSettingsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return GlobalResponse<UnionAdminSettingsResponse>.Success(
                await unionService.UpdateSettingsAsync(request, cancellationToken));
        }
        catch (UnionException exception)
        {
            return GlobalResponse<UnionAdminSettingsResponse>.ResultError(message: exception.Message);
        }
    }

    /// <summary>Admin-triggered validated center directory refresh, using the frontend response contract.</summary>
    [HttpPost("updatelist")]
    public Task<GlobalResponse<object>> UpdateList(CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => unionService.UpdateServerListAsync(cancellationToken));
    }

    /// <summary>Admin-triggered texture-only key refresh; successful responses contain no private material.</summary>
    [HttpPost("updateprivatekey")]
    public Task<GlobalResponse<object>> UpdatePrivateKey(CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => unionService.UpdatePrivateKeyAsync(cancellationToken));
    }

    /// <summary>Forces a full UUID/name snapshot while retaining retry state if the center is offline.</summary>
    [HttpPost("sync")]
    public Task<GlobalResponse<object>> Sync(CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => unionService.SynchronizeProfilesAsync(true, cancellationToken));
    }

    /// <summary>Returns only center operation status; upstream headers and response bodies are intentionally withheld.</summary>
    [HttpPost("diagnose")]
    public async Task<GlobalResponse<UnionAdminOperationResponse>> Diagnose(
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await unionService.DiagnoseCenterAsync(cancellationToken);
            return response.Status == "ok"
                ? GlobalResponse<UnionAdminOperationResponse>.Success(response)
                : GlobalResponse<UnionAdminOperationResponse>.ResultError(response, "Union diagnostics failed.");
        }
        catch (UnionException exception)
        {
            return GlobalResponse<UnionAdminOperationResponse>.ResultError(message: exception.Message);
        }
    }

    /// <summary>Converts safe Union business errors into the application envelope without forwarding protocol HTTP statuses.</summary>
    private static async Task<GlobalResponse<object>> ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
            return GlobalResponse<object>.Success();
        }
        catch (UnionException exception)
        {
            return GlobalResponse<object>.ResultError(message: exception.Message);
        }
    }
}

