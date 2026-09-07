using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service;

namespace Renatus.Host.Controller;

[ApiController]
[Route("api/site")]
public sealed class SiteController(SiteService siteService) : ControllerBase
{
    [HttpGet("config")]
    public async Task<GlobalResponse<SiteConfigDto>> GetConfig(CancellationToken cancellationToken)
        => GlobalResponse<SiteConfigDto>.Success(await siteService.GetConfigAsync(cancellationToken));
}
