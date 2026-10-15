using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service.Union;
using Renatus.Host.Utils;

namespace Renatus.Host.Controller.Union;

[ApiController]
public sealed class UnionActivityController(UnionActivityService activityService) : ControllerBase
{
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/union/activity")]
    public async Task<GlobalResponse<UnionActivityPageResponse>> UserActivity(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return GlobalResponse<UnionActivityPageResponse>.ResultError(message: "Authorization token is invalid.");
        }
        return await ExecuteAsync(() => activityService.GetUserPageAsync(userId, page, pageSize, cancellationToken));
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    [HttpGet("api/admin/union/activity")]
    public Task<GlobalResponse<UnionActivityPageResponse>> AdminActivity(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => activityService.GetAdminPageAsync(page, pageSize, cancellationToken));

    private static async Task<GlobalResponse<UnionActivityPageResponse>> ExecuteAsync(
        Func<Task<UnionActivityPageResponse>> action)
    {
        try
        {
            return GlobalResponse<UnionActivityPageResponse>.Success(await action());
        }
        catch (UnionException exception)
        {
            return GlobalResponse<UnionActivityPageResponse>.ResultError(message: exception.Message);
        }
    }
}
