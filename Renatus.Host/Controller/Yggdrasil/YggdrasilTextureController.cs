using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Service;

namespace Renatus.Host.Controller.Yggdrasil;

[Route("api/yggdrasil/textures")]
public sealed class YggdrasilTextureController(YggdrasilService yggdrasilService) : ControllerBase
{
    [HttpGet("{id}.png")]
    public async Task<IActionResult> GetTexture(string id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(id, "N", out var textureId))
        {
            return NotFound();
        }

        var texture = await yggdrasilService.GetTextureAsync(textureId, cancellationToken);
        if (texture is null)
        {
            return NotFound();
        }

        return File(Convert.FromBase64String(texture.DataBase64), texture.ContentType);
    }
}
