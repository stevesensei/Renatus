using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service;
using Renatus.Host.Utils;

namespace Renatus.Host.Controller.Yggdrasil;

[Route("api/yggdrasil/sessionserver/session/minecraft")]
public sealed class YggdrasilSessionController(YggdrasilService yggdrasilService)
    : YggdrasilControllerBase
{
    [HttpPost("join")]
    public async Task<IActionResult> Join(JoinRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await yggdrasilService.JoinAsync(request, cancellationToken);
            return NoContent();
        }
        catch (YggdrasilException exception)
        {
            return YggdrasilError(exception);
        }
    }

    [HttpGet("hasJoined")]
    public async Task<ActionResult<SessionProfileResponse>> HasJoined(
        [FromQuery] string username,
        [FromQuery] string serverId,
        CancellationToken cancellationToken)
    {
        var response = await yggdrasilService.HasJoinedAsync(username, serverId, cancellationToken);
        return response is null ? NoContent() : Ok(response);
    }

    [HttpGet("profile/{id}")]
    public async Task<ActionResult<SessionProfileResponse>> Profile(
        string id,
        [FromQuery] string? unsigned,
        CancellationToken cancellationToken)
    {
        try
        {
            var playerId = YggdrasilFormat.ParseUnsignedUuid(id);
            var response = await yggdrasilService.GetSessionProfileAsync(playerId, unsigned != "false", cancellationToken);
            return response is null ? NoContent() : Ok(response);
        }
        catch (YggdrasilException exception)
        {
            return YggdrasilError(exception);
        }
    }
}
