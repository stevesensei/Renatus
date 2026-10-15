using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service.Union;
using Renatus.Host.Utils;

namespace Renatus.Host.Controller.Union;

[ApiController]
[Route("api/union")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class UnionUserController(UnionUserService unionUserService) : ControllerBase
{
    [HttpGet("profiles")]
    public Task<GlobalResponse<UnionProfilesResponse>> Profiles(CancellationToken cancellationToken) =>
        ExecuteAsync(userId => unionUserService.GetProfilesAsync(userId, cancellationToken));

    [HttpGet("directory")]
    public async Task<GlobalResponse<UnionDirectoryResponse>> Directory(CancellationToken cancellationToken) =>
        await ExecuteWithoutUserAsync(() => unionUserService.GetDirectoryAsync(cancellationToken));

    [HttpGet("security-level")]
    public async Task<GlobalResponse<UnionSecurityLevelResponse>> SecurityLevel(CancellationToken cancellationToken) =>
        await ExecuteWithoutUserAsync(() => unionUserService.GetSecurityLevelAsync(cancellationToken));

    [HttpPost("profiles/{playerId:guid}/bind-token")]
    public Task<GlobalResponse<UnionBindTokenResponse>> BindToken(Guid playerId, CancellationToken cancellationToken) =>
        ExecuteAsync(userId => unionUserService.CreateBindTokenAsync(userId, playerId, cancellationToken));

    [HttpPost("profiles/{playerId:guid}/bind")]
    public Task<GlobalResponse<object>> Bind(
        Guid playerId, UnionBindRequest request, CancellationToken cancellationToken) =>
        ExecuteEmptyAsync(userId => unionUserService.BindAsync(userId, playerId, request, cancellationToken));

    [HttpPost("profiles/{playerId:guid}/unbind")]
    public Task<GlobalResponse<object>> Unbind(Guid playerId, CancellationToken cancellationToken) =>
        ExecuteEmptyAsync(userId => unionUserService.UnbindAsync(userId, playerId, cancellationToken));

    [HttpPost("profiles/{playerId:guid}/remap")]
    public Task<GlobalResponse<object>> Remap(
        Guid playerId, UnionRemapProfileRequest request, CancellationToken cancellationToken) =>
        ExecuteEmptyAsync(userId => unionUserService.RemapAsync(userId, playerId, request, cancellationToken));

    private async Task<GlobalResponse<T>> ExecuteAsync<T>(Func<Guid, Task<T>> action)
    {
        if (!TryGetUserId(out var userId)) return GlobalResponse<T>.ResultError(message: "Authorization token is invalid.");
        try
        {
            return GlobalResponse<T>.Success(await action(userId));
        }
        catch (UnionException exception)
        {
            return GlobalResponse<T>.ResultError(message: exception.Message);
        }
    }

    private static async Task<GlobalResponse<T>> ExecuteWithoutUserAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return GlobalResponse<T>.Success(await action());
        }
        catch (UnionException exception)
        {
            return GlobalResponse<T>.ResultError(message: exception.Message);
        }
    }

    private async Task<GlobalResponse<object>> ExecuteEmptyAsync(Func<Guid, Task> action)
    {
        if (!TryGetUserId(out var userId)) return GlobalResponse<object>.ResultError(message: "Authorization token is invalid.");
        try
        {
            await action(userId);
            return GlobalResponse<object>.Success();
        }
        catch (UnionException exception)
        {
            return GlobalResponse<object>.ResultError(message: exception.Message);
        }
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
