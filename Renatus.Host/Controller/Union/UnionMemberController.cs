using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Renatus.Host.Data;
using Renatus.Host.Service.Union;
using Renatus.Host.Utils;

namespace Renatus.Host.Controller.Union;

[ApiController]
[Route("api/union/member")]
public sealed class UnionMemberController(UnionService unionService) : ControllerBase
{
    [HttpGet]
    public Task<UnionMemberInfoResponse> Get(CancellationToken cancellationToken)
    {
        return unionService.GetMemberInfoAsync(cancellationToken);
    }

    [HttpPost("updatelist")]
    [ServiceFilter<UnionSignatureFilter>]
    public Task<IActionResult> UpdateList(CancellationToken cancellationToken)
    {
        return ExecuteNoContentAsync(
            () => unionService.UpdateServerListAsync(cancellationToken));
    }

    [HttpPost("updateprivatekey")]
    [ServiceFilter<UnionSignatureFilter>]
    public Task<IActionResult> UpdatePrivateKey(CancellationToken cancellationToken)
    {
        return ExecuteNoContentAsync(
            () => unionService.UpdatePrivateKeyAsync(cancellationToken));
    }

    [HttpPost("updatebackendkey")]
    [ServiceFilter<UnionSignatureFilter>]
    public Task<IActionResult> UpdateBackendKey(
        UnionBackendKeyRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteNoContentAsync(
            () => unionService.UpdateMemberKeyAsync(request.Key, cancellationToken));
    }

    [HttpPost("sync")]
    [ServiceFilter<UnionSignatureFilter>]
    public Task<IActionResult> Sync(CancellationToken cancellationToken)
    {
        return ExecuteNoContentAsync(
            () => unionService.SynchronizeProfilesAsync(true, cancellationToken));
    }

    [HttpPost("remapuuid")]
    [ServiceFilter<UnionSignatureFilter>]
    public Task<IActionResult> RemapUuid(
        UnionRemapRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteNoContentAsync(
            () => unionService.ApplyRemapAsync(request.RemappedUuid, cancellationToken));
    }

    [HttpGet("queryemail")]
    [ServiceFilter<UnionSignatureFilter>]
    public async Task<IActionResult> QueryEmail(
        [FromQuery] string? username,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await unionService.QueryEmailAsync(username, cancellationToken);
            return response is null ? NoContent() : Ok(response);
        }
        catch (UnionException exception)
        {
            return ProtocolError(exception);
        }
    }

    [HttpPost("diagnose")]
    [ServiceFilter<UnionSignatureFilter>]
    public IActionResult Diagnose(UnionDiagnoseRequest request)
    {
        return Ok(new UnionDiagnoseResponse(
            request.Nonce,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d));
    }

    private async Task<IActionResult> ExecuteNoContentAsync(Func<Task> action)
    {
        try
        {
            await action();
            return NoContent();
        }
        catch (UnionException exception)
        {
            return ProtocolError(exception);
        }
    }

    private ObjectResult ProtocolError(UnionException exception)
    {
        return StatusCode(exception.StatusCode, new { error = exception.Message });
    }
}
