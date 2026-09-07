using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Service;

namespace Renatus.Host.Controller.Yggdrasil;

[Route("api/yggdrasil")]
public sealed class YggdrasilMetadataController(YggdrasilService yggdrasilService) : ControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Root(CancellationToken cancellationToken)
    {
        return Ok(await yggdrasilService.GetMetadataAsync(cancellationToken));
    }
}
