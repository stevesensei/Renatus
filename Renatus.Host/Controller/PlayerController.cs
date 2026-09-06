using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service;
using Renatus.Host.Utils;

namespace Renatus.Host.Controller;

[ApiController]
[Route("api/player")]
[Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)]
public sealed class PlayerController(PlayerService playerService) : ControllerBase
{
    [HttpPost]
    public async Task<GlobalResponse<PlayerDto>> CreatePlayer(
        CreatePlayerRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return GlobalResponse<PlayerDto>.ResultError(message: "Authorization token is invalid.");
        }

        try
        {
            var response = await playerService.CreatePlayerAsync(userId, request, cancellationToken);
            return GlobalResponse<PlayerDto>.Success(response);
        }
        catch (PlayerException exception)
        {
            return GlobalResponse<PlayerDto>.ResultError(message: exception.Message);
        }
    }

    [HttpGet("{playerId:guid}")]
    public async Task<GlobalResponse<PlayerDto>> GetPlayer(Guid playerId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return GlobalResponse<PlayerDto>.ResultError(message: "Authorization token is invalid.");
        }

        try
        {
            var response = await playerService.GetPlayerAsync(userId, playerId, cancellationToken);
            return GlobalResponse<PlayerDto>.Success(response);
        }
        catch (PlayerException exception)
        {
            return GlobalResponse<PlayerDto>.ResultError(message: exception.Message);
        }
    }

    [HttpGet]
    public async Task<GlobalResponse<UserPlayersResponse>> GetCurrentUserPlayers(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return GlobalResponse<UserPlayersResponse>.ResultError(message: "Authorization token is invalid.");
        }

        try
        {
            var response = await playerService.GetUserPlayersAsync(userId, cancellationToken);
            return GlobalResponse<UserPlayersResponse>.Success(response);
        }
        catch (PlayerException exception)
        {
            return GlobalResponse<UserPlayersResponse>.ResultError(message: exception.Message);
        }
    }

    [HttpPut("{playerId:guid}/textures")]
    public async Task<GlobalResponse<PlayerDto>> UpdatePlayerTextures(
        Guid playerId,
        UpdatePlayerTexturesRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return GlobalResponse<PlayerDto>.ResultError(message: "Authorization token is invalid.");
        }

        try
        {
            var response = await playerService.UpdatePlayerTexturesAsync(userId, playerId, request, cancellationToken);
            return GlobalResponse<PlayerDto>.Success(response);
        }
        catch (PlayerException exception)
        {
            return GlobalResponse<PlayerDto>.ResultError(message: exception.Message);
        }
    }

    [HttpDelete("{playerId:guid}")]
    public async Task<GlobalResponse<DeletePlayerResponse>> DeletePlayer(Guid playerId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return GlobalResponse<DeletePlayerResponse>.ResultError(message: "Authorization token is invalid.");
        }

        try
        {
            var response = await playerService.DeletePlayerAsync(userId, playerId, cancellationToken);
            return GlobalResponse<DeletePlayerResponse>.Success(response);
        }
        catch (PlayerException exception)
        {
            return GlobalResponse<DeletePlayerResponse>.ResultError(message: exception.Message);
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdValue, out userId);
    }
}
