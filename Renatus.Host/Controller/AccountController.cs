using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service;
using Renatus.Host.Utils;

namespace Renatus.Host.Controller;

[ApiController]
[Route("api/account")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class AccountController(AuthService authService) : ControllerBase
{
    [HttpGet]
    public async Task<GlobalResponse<AccountProfileDto>> Get(CancellationToken cancellationToken)
    {
        try
        {
            var result = await authService.GetAccountProfileAsync(GetUserId(), cancellationToken);
            return GlobalResponse<AccountProfileDto>.Success(result);
        }
        catch (AuthException exception)
        {
            return GlobalResponse<AccountProfileDto>.ResultError(message: exception.Message);
        }
    }

    [HttpPut]
    public async Task<GlobalResponse<AccountProfileDto>> Update(
        UpdateAccountProfileRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await authService.UpdateAccountProfileAsync(GetUserId(), request, cancellationToken);
            return GlobalResponse<AccountProfileDto>.Success(result);
        }
        catch (AuthException exception)
        {
            return GlobalResponse<AccountProfileDto>.ResultError(message: exception.Message);
        }
    }

    [HttpPut("password")]
    public async Task<GlobalResponse<object>> ChangePassword(
        ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await authService.ChangePasswordAsync(GetUserId(), request, cancellationToken);
            return GlobalResponse<object>.Success();
        }
        catch (AuthException exception)
        {
            return GlobalResponse<object>.ResultError(message: exception.Message);
        }
    }

    private Guid GetUserId()
    {
        if (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return userId;
        throw new AuthException(StatusCodes.Status401Unauthorized, "Authorization token is invalid.");
    }
}

