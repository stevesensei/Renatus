using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service;
using Renatus.Host.Utils;

namespace Renatus.Host.Controller;

[Route("api/yggdrasil/authserver")]
public sealed class YggdrasilAuthController(YggdrasilService yggdrasilService) : YggdrasilControllerBase
{
    [HttpPost("authenticate")]
    public async Task<ActionResult<AuthenticateResponse>> Authenticate(
        AuthenticateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await yggdrasilService.AuthenticateAsync(request, cancellationToken));
        }
        catch (YggdrasilException exception)
        {
            return YggdrasilError(exception);
        }
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<RefreshResponse>> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await yggdrasilService.RefreshAsync(request, cancellationToken));
        }
        catch (YggdrasilException exception)
        {
            return YggdrasilError(exception);
        }
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate(ValidateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await yggdrasilService.ValidateAsync(request, cancellationToken);
            return NoContent();
        }
        catch (YggdrasilException exception)
        {
            return YggdrasilError(exception);
        }
    }

    [HttpPost("invalidate")]
    public async Task<IActionResult> Invalidate(InvalidateRequest request, CancellationToken cancellationToken)
    {
        await yggdrasilService.InvalidateAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPost("signout")]
    public async Task<IActionResult> Signout(SignoutRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await yggdrasilService.SignoutAsync(request, cancellationToken);
            return NoContent();
        }
        catch (YggdrasilException exception)
        {
            return YggdrasilError(exception);
        }
    }
}
