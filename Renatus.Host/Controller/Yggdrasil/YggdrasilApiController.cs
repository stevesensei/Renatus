using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service;
using Renatus.Host.Utils;

namespace Renatus.Host.Controller.Yggdrasil;

[Route("api/yggdrasil/api")]
public sealed class YggdrasilApiController(YggdrasilService yggdrasilService) : YggdrasilControllerBase
{
    /// <summary>Shares the bounded normalized name lookup between legacy and minecraftservices routes and returns Minecraft UUIDs.</summary>
    [HttpPost("profiles/minecraft")]
    [HttpPost("/api/yggdrasil/minecraftservices/minecraft/profile/lookup/bulk/byname")]
    public async Task<ActionResult<IReadOnlyCollection<PlayerNameToIdResponse>>> ProfilesMinecraft(
        IReadOnlyCollection<string> playerNames,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await yggdrasilService.FindManyByNameAsync(playerNames, cancellationToken));
        }
        catch (YggdrasilException exception)
        {
            return YggdrasilError(exception);
        }
    }

    /// <summary>Uses the same public Minecraft UUID lookup for both protocol aliases without frontend Bearer authentication.</summary>
    [HttpGet("users/profiles/minecraft/{playerName}")]
    [HttpGet("/api/yggdrasil/minecraftservices/minecraft/profile/lookup/name/{playerName}")]
    public async Task<ActionResult<PlayerNameToIdResponse>> UserProfileMinecraft(
        string playerName,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await yggdrasilService.FindByNameAsync(playerName, cancellationToken);
            return response is null ? NoContent() : Ok(response);
        }
        catch (YggdrasilException exception)
        {
            return YggdrasilError(exception);
        }
    }

    [HttpGet("user/security/location")]
    public IActionResult SecurityLocation()
    {
        return NoContent();
    }
}
