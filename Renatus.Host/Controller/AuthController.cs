using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service;
using Renatus.Host.Utils;

namespace Renatus.Host.Controller;

[ApiController]
[Route("/api/auth")]
public sealed class AuthController(
    AuthService authService,
    RegistrationVerificationService registrationVerificationService) : ControllerBase
{
    [HttpPost("register-code")]
    public async Task<GlobalResponse<RegistrationCodeResponse>> SendRegistrationCode(
        SendRegistrationCodeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registrationVerificationService.SendAsync(
                request.Email, HttpContext.Connection.RemoteIpAddress, cancellationToken);
            return GlobalResponse<RegistrationCodeResponse>.Success(response);
        }
        catch (AuthException exception)
        {
            return exception.StatusCode >= 500
                ? GlobalResponse<RegistrationCodeResponse>.Error(message: exception.Message)
                : GlobalResponse<RegistrationCodeResponse>.ResultError(message: exception.Message);
        }
    }

    [HttpPost("session-key")]
    public async Task<GlobalResponse<SessionKeyResponse>> CreateSessionKey(CancellationToken cancellationToken)
    {
        var response = await authService.CreateSessionKeyAsync(cancellationToken);
        return GlobalResponse<SessionKeyResponse>.Success(response);
    }

    [HttpPost("login")]
    public async Task<GlobalResponse<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await authService.LoginAsync(request, cancellationToken);
            return GlobalResponse<AuthResponse>.Success(response);
        }
        catch (AuthException exception)
        {
            return GlobalResponse<AuthResponse>.ResultError(message: exception.Message);
        }
    }

    [HttpPost("register")]
    public async Task<GlobalResponse<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await authService.RegisterAsync(request, cancellationToken);
            return GlobalResponse<AuthResponse>.Success(response);
        }
        catch (AuthException exception)
        {
            return GlobalResponse<AuthResponse>.ResultError(message: exception.Message);
        }
    }

    [Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("me")]
    public async Task<GlobalResponse<AuthUserDto>> Me(CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return GlobalResponse<AuthUserDto>.ResultError(message: "Authorization token is invalid.");
        }

        try
        {
            var response = await authService.GetUserAsync(userId, cancellationToken);
            return GlobalResponse<AuthUserDto>.Success(response);
        }
        catch (AuthException exception)
        {
            return GlobalResponse<AuthUserDto>.ResultError(message: exception.Message);
        }
    }

    [Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    [HttpGet("admin/ping")]
    public Task<GlobalResponse<object>> AdminPing()
    {
        return Task.FromResult(GlobalResponse<object>.Success());
    }

    [Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme, Roles = "Player")]
    [HttpGet("player/ping")]
    public Task<GlobalResponse<object>> PlayerPing()
    {
        return Task.FromResult(GlobalResponse<object>.Success());
    }
}
