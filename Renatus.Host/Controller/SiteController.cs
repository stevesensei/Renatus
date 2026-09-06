using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service;

namespace Renatus.Host.Controller;

[ApiController]
[Route("api/site")]
public sealed class SiteController(SiteService siteService) : ControllerBase
{
    [HttpGet("config")]
    public GlobalResponse<SiteConfigDto> GetConfig() => GlobalResponse<SiteConfigDto>.Success(siteService.GetConfig());
}
