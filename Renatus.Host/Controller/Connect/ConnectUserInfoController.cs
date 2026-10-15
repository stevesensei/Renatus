using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Validation.AspNetCore;
using Renatus.Host.Service.Connect;

namespace Renatus.Host.Controller.Connect;

[ApiController]
public sealed class ConnectUserInfoController(
    ConnectProtocolService protocolService,
    IAuthenticationSchemeProvider schemeProvider) : ControllerBase
{
    [AcceptVerbs("GET", "POST")]
    [Route("~/connect/userinfo")]
    public async Task<IActionResult> UserInfo(CancellationToken cancellationToken)
    {
        if (await schemeProvider.GetSchemeAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme) is null)
            return NotFound();
        var result = await HttpContext.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal is null)
            return Challenge(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        try
        {
            return Ok(await protocolService.GetUserInfoAsync(result.Principal, cancellationToken));
        }
        catch (ConnectException)
        {
            return Unauthorized();
        }
    }
}
