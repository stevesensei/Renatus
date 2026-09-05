using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Service;

namespace Renatus.Host.Controller;

[Route("api/yggdrasil")]
public sealed class YggdrasilMetadataController(YggdrasilService yggdrasilService) : ControllerBase
{
    [HttpGet("")]
    public IActionResult Root()
    {
        return Ok(yggdrasilService.GetMetadata());
    }
}
