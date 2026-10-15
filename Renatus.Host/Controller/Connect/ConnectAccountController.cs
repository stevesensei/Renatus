using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service.Connect;

namespace Renatus.Host.Controller.Connect;

[ApiController]
[Route("api/connect")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ConnectAccountController(ConnectManagementService service) : ControllerBase
{
    [HttpGet("interactions/{interactionId}")]
    public async Task<GlobalResponse<ConnectInteractionDto>> GetInteraction(
        string interactionId, CancellationToken cancellationToken)
        => await ResultAsync(() => service.GetInteractionAsync(UserId(), interactionId, cancellationToken));

    [HttpPost("interactions/{interactionId}/decision")]
    public async Task<GlobalResponse<ConnectCompletionDto>> Decide(
        string interactionId, ConnectDecisionRequest request, CancellationToken cancellationToken)
        => await ResultAsync(() => service.DecideAsync(UserId(), interactionId, request, cancellationToken));

    [HttpGet("grants")]
    public async Task<GlobalResponse<IReadOnlyList<ConnectGrantDto>>> GetGrants(CancellationToken cancellationToken)
        => await ResultAsync(() => service.GetGrantsAsync(UserId(), cancellationToken));

    [HttpDelete("grants/{authorizationId}")]
    public async Task<GlobalResponse<object>> RevokeGrant(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            await service.RevokeGrantAsync(UserId(), authorizationId, cancellationToken);
            return GlobalResponse<object>.Success();
        }
        catch (ConnectException exception)
        {
            return GlobalResponse<object>.ResultError(message: exception.Message);
        }
    }

    private Guid UserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId : throw new ConnectException("Authorization token is invalid.");

    private static async Task<GlobalResponse<T>> ResultAsync<T>(Func<Task<T>> operation)
    {
        try { return GlobalResponse<T>.Success(await operation()); }
        catch (ConnectException exception) { return GlobalResponse<T>.ResultError(message: exception.Message); }
    }
}
