using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service;
using Renatus.Host.Utils;

namespace Renatus.Host.Controller;

[ApiController]
[Route("/api/auth")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("session-key")]
    public async Task<ActionResult<SessionKeyResponse>> CreateSessionKey(CancellationToken cancellationToken)
    {
        return Ok(await authService.CreateSessionKeyAsync(cancellationToken));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authService.LoginAsync(request, cancellationToken));
        }
        catch (AuthException exception)
        {
            return StatusCode(exception.StatusCode, new AuthErrorResponse(exception.Message));
        }
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authService.RegisterAsync(request, cancellationToken));
        }
        catch (AuthException exception)
        {
            return StatusCode(exception.StatusCode, new AuthErrorResponse(exception.Message));
        }
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<AuthUserDto>> Me(CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Unauthorized(new AuthErrorResponse("Authorization token is invalid."));
        }

        try
        {
            return Ok(await authService.GetUserAsync(userId, cancellationToken));
        }
        catch (AuthException exception)
        {
            return StatusCode(exception.StatusCode, new AuthErrorResponse(exception.Message));
        }
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("admin/ping")]
    public IActionResult AdminPing()
    {
        return NoContent();
    }

    [Authorize(Roles = "Player")]
    [HttpGet("player/ping")]
    public IActionResult PlayerPing()
    {
        return NoContent();
    }
}
