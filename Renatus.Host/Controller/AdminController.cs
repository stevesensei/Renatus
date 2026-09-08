using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Entity;
using Renatus.Host.Service;
using Renatus.Host.Utils;

namespace Renatus.Host.Controller;

[ApiController]
[Route("api/admin")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public sealed class AdminController(
    AuthService authService,
    PlayerService playerService,
    TextureService textureService,
    YggdrasilService yggdrasilService) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<GlobalResponse<AdminOverviewDto>> GetOverview(CancellationToken cancellationToken)
    {
        var totalUsers = await authService.CountUsersAsync(cancellationToken);
        var totalPlayers = await playerService.CountPlayersAsync(cancellationToken);
        var totalTextures = await textureService.CountTexturesAsync(cancellationToken);
        var sessionOverview = await yggdrasilService.GetSessionOverviewAsync(10, cancellationToken);

        var overview = new AdminOverviewDto(
            totalUsers,
            totalPlayers,
            totalTextures,
            sessionOverview.TotalClients,
            sessionOverview.RecentActivities);
        return GlobalResponse<AdminOverviewDto>.Success(overview);
    }

    #region Users

    [HttpGet("users")]
    public async Task<GlobalResponse<AdminPageDto<AdminUserListItemDto>>> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] UserRole? role = null,
        [FromQuery] bool? isDisabled = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await authService.ListUsersAsync(page, pageSize, search, role, isDisabled, cancellationToken);
            return GlobalResponse<AdminPageDto<AdminUserListItemDto>>.Success(response);
        }
        catch (AuthException exception)
        {
            return GlobalResponse<AdminPageDto<AdminUserListItemDto>>.ResultError(message: exception.Message);
        }
    }

    [HttpGet("users/{id:guid}")]
    public async Task<GlobalResponse<AdminUserDetailDto>> GetUser(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var response = await authService.GetUserDetailAsync(id, cancellationToken);
            return GlobalResponse<AdminUserDetailDto>.Success(response);
        }
        catch (AuthException exception)
        {
            return GlobalResponse<AdminUserDetailDto>.ResultError(message: exception.Message);
        }
    }

    [HttpPatch("users/{id:guid}")]
    public async Task<GlobalResponse<AdminUserListItemDto>> UpdateUser(
        Guid id,
        AdminUpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await authService.UpdateUserAsync(id, request, cancellationToken);
            return GlobalResponse<AdminUserListItemDto>.Success(response);
        }
        catch (AuthException exception)
        {
            return GlobalResponse<AdminUserListItemDto>.ResultError(message: exception.Message);
        }
    }

    [HttpPost("users/{id:guid}/password")]
    public async Task<GlobalResponse<object>> ResetPassword(
        Guid id,
        AdminResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await authService.ResetPasswordAsync(id, request.NewPassword, cancellationToken);
            return GlobalResponse<object>.Success();
        }
        catch (AuthException exception)
        {
            return GlobalResponse<object>.ResultError(message: exception.Message);
        }
    }

    [HttpPost("users/{id:guid}/sessions/revoke")]
    public async Task<GlobalResponse<AdminRevokeSessionsResponse>> RevokeUserSessions(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            await yggdrasilService.RevokeAllUserSessionsAsync(id, cancellationToken);
            return GlobalResponse<AdminRevokeSessionsResponse>.Success(new AdminRevokeSessionsResponse(id));
        }
        catch (AuthException exception)
        {
            return GlobalResponse<AdminRevokeSessionsResponse>.ResultError(message: exception.Message);
        }
    }

    [HttpDelete("users/{id:guid}")]
    public async Task<GlobalResponse<AdminDeleteUserResponse>> DeleteUser(
        Guid id,
        [FromQuery] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        if (!confirm)
        {
            return GlobalResponse<AdminDeleteUserResponse>.ResultError(
                message: "Deleting a user is irreversible. Confirm with ?confirm=true.");
        }

        try
        {
            await authService.DeleteUserAsync(id, cancellationToken);
            return GlobalResponse<AdminDeleteUserResponse>.Success(new AdminDeleteUserResponse(id));
        }
        catch (AuthException exception)
        {
            return GlobalResponse<AdminDeleteUserResponse>.ResultError(message: exception.Message);
        }
    }

    #endregion

    #region Players

    [HttpGet("players")]
    public async Task<GlobalResponse<AdminPageDto<AdminPlayerListItemDto>>> GetPlayers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? ownerId = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await playerService.ListPlayersAsync(page, pageSize, ownerId, search, cancellationToken);
            return GlobalResponse<AdminPageDto<AdminPlayerListItemDto>>.Success(response);
        }
        catch (PlayerException exception)
        {
            return GlobalResponse<AdminPageDto<AdminPlayerListItemDto>>.ResultError(message: exception.Message);
        }
    }

    [HttpGet("players/{id:guid}")]
    public async Task<GlobalResponse<AdminPlayerDetailDto>> GetPlayer(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var response = await playerService.GetPlayerDetailAsync(id, cancellationToken);
            return GlobalResponse<AdminPlayerDetailDto>.Success(response);
        }
        catch (PlayerException exception)
        {
            return GlobalResponse<AdminPlayerDetailDto>.ResultError(message: exception.Message);
        }
    }

    [HttpPatch("players/{id:guid}")]
    public async Task<GlobalResponse<AdminPlayerDetailDto>> UpdatePlayer(
        Guid id,
        AdminUpdatePlayerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await playerService.UpdatePlayerAsync(id, request, cancellationToken);
            return GlobalResponse<AdminPlayerDetailDto>.Success(response);
        }
        catch (PlayerException exception)
        {
            return GlobalResponse<AdminPlayerDetailDto>.ResultError(message: exception.Message);
        }
    }

    [HttpDelete("players/{id:guid}")]
    public async Task<GlobalResponse<AdminDeletePlayerResponse>> DeletePlayer(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            await playerService.DeletePlayerAdminAsync(id, cancellationToken);
            return GlobalResponse<AdminDeletePlayerResponse>.Success(new AdminDeletePlayerResponse(id));
        }
        catch (PlayerException exception)
        {
            return GlobalResponse<AdminDeletePlayerResponse>.ResultError(message: exception.Message);
        }
    }

    #endregion

    #region Textures

    [HttpGet("textures")]
    public async Task<GlobalResponse<AdminPageDto<AdminTextureListItemDto>>> GetTextures(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? isPublic = null,
        [FromQuery] TextureType? type = null,
        [FromQuery] Guid? uploaderId = null,
        [FromQuery] TextureSource? source = null,
        [FromQuery] string? sha256 = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await textureService.ListTexturesAsync(
                page, pageSize, isPublic, type, uploaderId, source, sha256, cancellationToken);
            return GlobalResponse<AdminPageDto<AdminTextureListItemDto>>.Success(response);
        }
        catch (TextureException exception)
        {
            return GlobalResponse<AdminPageDto<AdminTextureListItemDto>>.ResultError(message: exception.Message);
        }
    }

    [HttpGet("textures/{id:guid}")]
    public async Task<GlobalResponse<AdminTextureDetailDto>> GetTexture(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var response = await textureService.GetTextureDetailAsync(id, cancellationToken);
            return GlobalResponse<AdminTextureDetailDto>.Success(response);
        }
        catch (TextureException exception)
        {
            return GlobalResponse<AdminTextureDetailDto>.ResultError(message: exception.Message);
        }
    }

    [HttpPatch("textures/{id:guid}")]
    public async Task<GlobalResponse<AdminTextureDetailDto>> UpdateTexture(
        Guid id,
        AdminUpdateTextureRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await textureService.UpdateTextureAsync(id, request, cancellationToken);
            return GlobalResponse<AdminTextureDetailDto>.Success(response);
        }
        catch (TextureException exception)
        {
            return GlobalResponse<AdminTextureDetailDto>.ResultError(message: exception.Message);
        }
    }

    [HttpDelete("textures/{id:guid}")]
    public async Task<GlobalResponse<AdminDeleteTextureResponse>> DeleteTexture(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            await textureService.DeleteTextureAdminAsync(id, cancellationToken);
            return GlobalResponse<AdminDeleteTextureResponse>.Success(new AdminDeleteTextureResponse(id));
        }
        catch (TextureException exception)
        {
            return GlobalResponse<AdminDeleteTextureResponse>.ResultError(message: exception.Message);
        }
    }

    #endregion
}