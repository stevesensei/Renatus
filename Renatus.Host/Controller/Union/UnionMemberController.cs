using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service.Union;
using Renatus.Host.Utils;

namespace Renatus.Host.Controller.Union;

[ApiController]
[Route("api/union/member")]
public sealed class UnionMemberController(UnionService unionService) : ControllerBase
{
    /// <summary>Returns public PHP-compatible member metadata; no member key, email, or private material is exposed.</summary>
    [HttpGet]
    public Task<UnionMemberInfoResponse> Get(CancellationToken cancellationToken)
    {
        Response.Headers.AccessControlAllowOrigin = "*";
        return unionService.GetMemberInfoAsync(cancellationToken);
    }

    /// <summary>Signed callback refreshes the complete center directory; success uses the protocol 204 response.</summary>
    [HttpPost("updatelist")]
    [ServiceFilter<UnionSignatureFilter>]
    public Task<IActionResult> UpdateList(CancellationToken cancellationToken)
    {
        return ExecuteNoContentAsync(
            () => unionService.UpdateServerListAsync(cancellationToken));
    }

    /// <summary>Signed callback updates shared texture signing material while preserving frontend and Yggdrasil login keys.</summary>
    [HttpPost("updateprivatekey")]
    [ServiceFilter<UnionSignatureFilter>]
    public Task<IActionResult> UpdatePrivateKey(CancellationToken cancellationToken)
    {
        return ExecuteNoContentAsync(
            () => unionService.UpdatePrivateKeyAsync(cancellationToken));
    }

    /// <summary>Signed callback accepts the exact PHP key field as a write-only outgoing member secret.</summary>
    [HttpPost("updatebackendkey")]
    [ServiceFilter<UnionSignatureFilter>]
    public Task<IActionResult> UpdateBackendKey(
        UnionBackendKeyRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteNoContentAsync(
            () => unionService.UpdateMemberKeyAsync(request.Key, cancellationToken));
    }

    /// <summary>Signed callback requests a full snapshot; center failures leave dirty state available for background retries.</summary>
    [HttpPost("sync")]
    [ServiceFilter<UnionSignatureFilter>]
    public Task<IActionResult> Sync(CancellationToken cancellationToken)
    {
        return ExecuteNoContentAsync(
            () => unionService.SynchronizeProfilesAsync(true, cancellationToken));
    }

    /// <summary>Signed callback applies the complete remapped_uuid batch atomically; conflicts reject every mapping.</summary>
    [HttpPost("remapuuid")]
    [ServiceFilter<UnionSignatureFilter>]
    public Task<IActionResult> RemapUuid(
        UnionRemapRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteNoContentAsync(
            () => unionService.ApplyRemapAsync(request.RemappedUuid, cancellationToken));
    }

    /// <summary>Signed center-only email lookup uses a normalized player name and returns 204 when no player matches.</summary>
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

    /// <summary>Signed diagnostic echoes the supplied JSON nonce and current fractional Unix seconds, matching PHP.</summary>
    [HttpPost("diagnose")]
    [ServiceFilter<UnionSignatureFilter>]
    public IActionResult Diagnose(UnionDiagnoseRequest request)
    {
        return Ok(new UnionDiagnoseResponse(
            request.Nonce,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d));
    }

    /// <summary>Maps business failures to safe Union protocol HTTP errors instead of frontend response envelopes.</summary>
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

