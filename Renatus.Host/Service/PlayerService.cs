using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Entity;
using Renatus.Host.Service.Cache;
using Renatus.Host.Utils;

namespace Renatus.Host.Service;

public sealed partial class PlayerService(AppDbContext dbContext, ICacheService cacheService)
{
    private static readonly TimeSpan UserPlayersCacheTtl = TimeSpan.FromMinutes(5);

    public async Task<PlayerDto> CreatePlayerAsync(
        Guid userId,
        CreatePlayerRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        ValidatePlayerName(name);

        var user = await dbContext.Users
            .Include(entity => entity.Players)
            .FirstOrDefaultAsync(entity => entity.Id == userId, cancellationToken);
        if (user is null || user.IsDisabled)
        {
            throw new PlayerException(StatusCodes.Status401Unauthorized, "User is unavailable.");
        }

        if (user.Players.Count >= user.MaxPlayerCount)
        {
            throw new PlayerException(StatusCodes.Status409Conflict, "Player creation limit has been reached.");
        }

        var normalizedName = YggdrasilFormat.Normalize(name);
        var nameExists = await dbContext.Players
            .AnyAsync(entity => entity.NormalizedName == normalizedName, cancellationToken);
        if (nameExists)
        {
            throw new PlayerException(StatusCodes.Status409Conflict, "Player name already exists.");
        }

        await EnsureTextureTypeAsync(request.SkinTextureId, TextureType.Skin, cancellationToken);
        await EnsureTextureTypeAsync(request.CapeTextureId, TextureType.Cape, cancellationToken);

        var player = new Player
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Name = name,
            NormalizedName = normalizedName,
            SkinModel = request.SkinModel,
            SkinTextureId = request.SkinTextureId,
            CapeTextureId = request.CapeTextureId
        };

        dbContext.Players.Add(player);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new PlayerException(StatusCodes.Status409Conflict, "Player name already exists.", exception);
        }

        await RemoveUserPlayersCacheAsync(user.Id, cancellationToken);
        return ToDto(player);
    }

    public async Task<PlayerDto> GetPlayerAsync(Guid userId, Guid playerId, CancellationToken cancellationToken)
    {
        var player = await dbContext.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(
                entity => entity.Id == playerId && entity.UserId == userId && !entity.User.IsDisabled,
                cancellationToken);
        if (player is null)
        {
            throw new PlayerException(StatusCodes.Status404NotFound, "Player was not found.");
        }

        return ToDto(player);
    }

    public async Task<UserPlayersResponse> GetUserPlayersAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .Where(entity => entity.Id == userId)
            .Select(entity => new
            {
                entity.IsDisabled,
                entity.MaxPlayerCount
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null || user.IsDisabled)
        {
            throw new PlayerException(StatusCodes.Status401Unauthorized, "User is unavailable.");
        }

        var cacheKey = UserPlayersCacheKey(userId);
        var cached = await cacheService.GetAsync<PlayerDto[]>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return new UserPlayersResponse(cached, user.MaxPlayerCount);
        }

        var players = await dbContext.Players
            .AsNoTracking()
            .Where(player => player.UserId == userId)
            .OrderBy(player => player.CreatedAt)
            .Select(player => ToDto(player))
            .ToArrayAsync(cancellationToken);

        await cacheService.SetAsync(cacheKey, players, UserPlayersCacheTtl, cancellationToken);
        return new UserPlayersResponse(players, user.MaxPlayerCount);
    }

    public async Task<PlayerDto> UpdatePlayerTexturesAsync(
        Guid userId,
        Guid playerId,
        UpdatePlayerTexturesRequest request,
        CancellationToken cancellationToken)
    {
        var player = await dbContext.Players
            .FirstOrDefaultAsync(
                entity => entity.Id == playerId && entity.UserId == userId && !entity.User.IsDisabled,
                cancellationToken);
        if (player is null)
        {
            throw new PlayerException(StatusCodes.Status404NotFound, "Player was not found.");
        }

        await EnsureTextureTypeAsync(request.SkinTextureId, TextureType.Skin, cancellationToken);
        await EnsureTextureTypeAsync(request.CapeTextureId, TextureType.Cape, cancellationToken);

        player.SkinTextureId = request.SkinTextureId;
        player.CapeTextureId = request.CapeTextureId;
        if (request.SkinModel is not null)
        {
            player.SkinModel = request.SkinModel.Value;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await RemoveUserPlayersCacheAsync(userId, cancellationToken);
        return ToDto(player);
    }

    public async Task<DeletePlayerResponse> DeletePlayerAsync(Guid userId, Guid playerId, CancellationToken cancellationToken)
    {
        var player = await dbContext.Players
            .FirstOrDefaultAsync(
                entity => entity.Id == playerId && entity.UserId == userId && !entity.User.IsDisabled,
                cancellationToken);
        if (player is null)
        {
            throw new PlayerException(StatusCodes.Status404NotFound, "Player was not found.");
        }

        dbContext.Players.Remove(player);
        await dbContext.SaveChangesAsync(cancellationToken);
        await RemoveUserPlayersCacheAsync(userId, cancellationToken);
        return new DeletePlayerResponse(playerId);
    }

    private async Task EnsureTextureTypeAsync(Guid? textureId, TextureType textureType, CancellationToken cancellationToken)
    {
        if (textureId is null)
        {
            return;
        }

        var exists = await dbContext.Textures
            .AsNoTracking()
            .AnyAsync(entity => entity.Id == textureId && entity.Type == textureType, cancellationToken);
        if (!exists)
        {
            throw new PlayerException(StatusCodes.Status404NotFound, $"{textureType} texture was not found.");
        }
    }

    private Task RemoveUserPlayersCacheAsync(Guid userId, CancellationToken cancellationToken)
    {
        return cacheService.RemoveAsync(UserPlayersCacheKey(userId), cancellationToken);
    }

    private static PlayerDto ToDto(Player player)
    {
        return new PlayerDto(
            player.Id,
            player.Name,
            player.SkinModel,
            player.SkinTextureId,
            player.CapeTextureId,
            player.CreatedAt,
            player.UpdatedAt);
    }

    private static string UserPlayersCacheKey(Guid userId)
    {
        return $"player:user:{userId:N}:players";
    }

    private static void ValidatePlayerName(string playerName)
    {
        if (!PlayerNameRegex().IsMatch(playerName))
        {
            throw new PlayerException(StatusCodes.Status400BadRequest, "Invalid player name.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_]{1,16}$")]
    private static partial Regex PlayerNameRegex();
}
