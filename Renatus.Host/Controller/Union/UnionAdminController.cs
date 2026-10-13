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
    [HttpGet("status")]
    public async Task<GlobalResponse<UnionAdminStatusResponse>> Status(CancellationToken cancellationToken)
    {
        return GlobalResponse<UnionAdminStatusResponse>.Success(
            await unionService.GetStatusAsync(cancellationToken));
    }

    [HttpGet("settings")]
    public async Task<GlobalResponse<UnionAdminSettingsResponse>> Settings(CancellationToken cancellationToken)
    {
        return GlobalResponse<UnionAdminSettingsResponse>.Success(
            await unionService.GetSettingsAsync(cancellationToken));
    }

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

    [HttpPost("updatelist")]
    public Task<GlobalResponse<object>> UpdateList(CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => unionService.UpdateServerListAsync(cancellationToken));
    }

    [HttpPost("updateprivatekey")]
    public Task<GlobalResponse<object>> UpdatePrivateKey(CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => unionService.UpdatePrivateKeyAsync(cancellationToken));
    }

    [HttpPost("sync")]
    public Task<GlobalResponse<object>> Sync(CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => unionService.SynchronizeProfilesAsync(true, cancellationToken));
    }

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
