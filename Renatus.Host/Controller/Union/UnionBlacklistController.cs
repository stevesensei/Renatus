using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service.Union;
using Renatus.Host.Utils;

namespace Renatus.Host.Controller.Union;

[ApiController]
[Route("api/admin/union/blacklist")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public sealed class UnionBlacklistController(UnionBlacklistService blacklistService) : ControllerBase
{
    [HttpGet]
    public async Task<GlobalResponse<JsonElement>> Query(
        [FromQuery(Name = "q")] string? query,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return GlobalResponse<JsonElement>.Success(
                await blacklistService.QueryAsync(query, page, cancellationToken));
        }
        catch (UnionException exception)
        {
            return GlobalResponse<JsonElement>.ResultError(message: exception.Message);
        }
    }

    [HttpPost]
    public Task<GlobalResponse<object>> Create(
        UnionBlacklistCreateRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => blacklistService.CreateAsync(request, cancellationToken));

    [HttpPost("{id:long}/invalidate")]
    public Task<GlobalResponse<object>> Invalidate(long id, CancellationToken cancellationToken) =>
        ExecuteAsync(() => blacklistService.InvalidateAsync(id, cancellationToken));

    [HttpDelete("{id:long}")]
    public Task<GlobalResponse<object>> Delete(long id, CancellationToken cancellationToken) =>
        ExecuteAsync(() => blacklistService.DeleteAsync(id, cancellationToken));

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
