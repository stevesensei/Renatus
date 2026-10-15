using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service;

namespace Renatus.Host.Controller;

[ApiController]
[Route("api/admin/yggdrasil/config")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public sealed class YggdrasilAdminConfigController(AppConfigurationService configurationService) : ControllerBase
{
    /// <summary>Returns the safe editable subset of Yggdrasil configuration through the frontend response envelope.</summary>
    [HttpGet]
    public async Task<GlobalResponse<YggdrasilAdminConfigDto>> Get(CancellationToken cancellationToken)
    {
        return GlobalResponse<YggdrasilAdminConfigDto>.Success(
            await configurationService.GetYggdrasilAdminConfigAsync(cancellationToken));
    }

    /// <summary>Validates and selectively updates safe fields; private keys and server metadata cannot be bound from this request.</summary>
    [HttpPut]
    public async Task<GlobalResponse<YggdrasilAdminConfigDto>> Put(
        UpdateYggdrasilAdminConfigRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return GlobalResponse<YggdrasilAdminConfigDto>.Success(
                await configurationService.UpdateYggdrasilAdminConfigAsync(request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return GlobalResponse<YggdrasilAdminConfigDto>.ResultError(message: exception.Message);
        }
    }
}
