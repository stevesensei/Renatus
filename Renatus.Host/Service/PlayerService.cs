using System.Text.RegularExpressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Entity;
using Renatus.Host.Service.Cache;
using Renatus.Host.Service.Union;
using Renatus.Host.Utils;

namespace Renatus.Host.Service;

public sealed partial class PlayerService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    ICacheService cacheService,
    IMapper mapper,
    AppConfigurationService configurationService,
    YggdrasilLifecycleService yggdrasilLifecycle,
    UnionService unionService)
{
    private static readonly TimeSpan UserPlayersCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PlayerCacheTtl = TimeSpan.FromMinutes(5);

    public async Task<PlayerDto> CreatePlayerAsync(
        Guid userId,
        CreatePlayerRequest request,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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

        await EnsureTextureTypeAsync(dbContext, user.Id, request.SkinTextureId, TextureType.Skin, cancellationToken);
        await EnsureTextureTypeAsync(dbContext, user.Id, request.CapeTextureId, TextureType.Cape, cancellationToken);

        var yggdrasilOptions = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
        var player = new Player
        {
            Id = Guid.NewGuid(),
            MinecraftUuid = yggdrasilOptions.UuidAlgorithm == "v3"
                ? YggdrasilFormat.CreateOfflinePlayerUuid(name)
                : Guid.NewGuid(),
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
        await unionService.MarkProfilesDirtyAsync(cancellationToken);
        return ToDto(player);
    }

    public async Task<PlayerDto> GetPlayerAsync(Guid userId, Guid playerId, CancellationToken cancellationToken)
    {
        var cacheKey = PlayerCacheKey(userId, playerId);
        var cached = await cacheService.GetAsync<PlayerDto>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var player = await dbContext.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(
                entity => entity.Id == playerId && entity.UserId == userId && !entity.User.IsDisabled,
                cancellationToken);
        if (player is null)
        {
            throw new PlayerException(StatusCodes.Status404NotFound, "Player was not found.");
        }

        var response = ToDto(player);
        await cacheService.SetAsync(cacheKey, response, PlayerCacheTtl, cancellationToken);
        return response;
    }

    public async Task<UserPlayersResponse> GetUserPlayersAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cacheKey = UserPlayersCacheKey(userId);
        var cached = await cacheService.GetAsync<UserPlayersResponse>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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

        var players = await dbContext.Players
            .AsNoTracking()
            .Where(player => player.UserId == userId)
            .OrderBy(player => player.CreatedAt)
            .Select(player => ToDto(player))
            .ToArrayAsync(cancellationToken);

        var response = new UserPlayersResponse(players, user.MaxPlayerCount);
        await cacheService.SetAsync(cacheKey, response, UserPlayersCacheTtl, cancellationToken);
        return response;
    }

    public async Task<PlayerDto> UpdatePlayerTexturesAsync(
        Guid userId,
        Guid playerId,
        UpdatePlayerTexturesRequest request,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var player = await dbContext.Players
            .FirstOrDefaultAsync(
                entity => entity.Id == playerId && entity.UserId == userId && !entity.User.IsDisabled,
                cancellationToken);
        if (player is null)
        {
            throw new PlayerException(StatusCodes.Status404NotFound, "Player was not found.");
        }

        await EnsureTextureTypeAsync(dbContext, userId, request.SkinTextureId, TextureType.Skin, cancellationToken);
        await EnsureTextureTypeAsync(dbContext, userId, request.CapeTextureId, TextureType.Cape, cancellationToken);

        player.SkinTextureId = request.SkinTextureId;
        player.CapeTextureId = request.CapeTextureId;
        if (request.SkinModel is not null)
        {
            player.SkinModel = request.SkinModel.Value;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await RemovePlayerCachesAsync(userId, playerId, cancellationToken);
        return ToDto(player);
    }

    public async Task<DeletePlayerResponse> DeletePlayerAsync(Guid userId, Guid playerId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var player = await dbContext.Players
            .FirstOrDefaultAsync(
                entity => entity.Id == playerId && entity.UserId == userId && !entity.User.IsDisabled,
                cancellationToken);
        if (player is null)
        {
            throw new PlayerException(StatusCodes.Status404NotFound, "Player was not found.");
        }

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            await yggdrasilLifecycle.RevokePlayerClientsAsync(dbContext, playerId, cancellationToken);
            dbContext.Players.Remove(player);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        await RemovePlayerCachesAsync(userId, playerId, cancellationToken);
        await unionService.MarkProfilesDirtyAsync(cancellationToken);
        return new DeletePlayerResponse(playerId);
    }

    private async Task EnsureTextureTypeAsync(
        AppDbContext dbContext,
        Guid userId,
        Guid? textureId,
        TextureType textureType,
        CancellationToken cancellationToken)
    {
        if (textureId is null)
        {
            return;
        }

        var exists = await dbContext.Textures
            .AsNoTracking()
            .AnyAsync(
                entity => entity.Id == textureId
                    && entity.Type == textureType
                    && (entity.UserId == userId || entity.IsPublic),
                cancellationToken);
        if (!exists)
        {
            throw new PlayerException(StatusCodes.Status404NotFound, $"{textureType} texture was not found.");
        }
    }

    private Task RemoveUserPlayersCacheAsync(Guid userId, CancellationToken cancellationToken)
    {
        return cacheService.RemoveAsync(UserPlayersCacheKey(userId), cancellationToken);
    }

    private async Task RemovePlayerCachesAsync(Guid userId, Guid playerId, CancellationToken cancellationToken)
    {
        await RemoveUserPlayersCacheAsync(userId, cancellationToken);
        await cacheService.RemoveAsync(PlayerCacheKey(userId, playerId), cancellationToken);
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

    public static string UserPlayersCacheKey(Guid userId)
    {
        return $"player:user:{userId:N}:players:v2";
    }

    public static string PlayerCacheKey(Guid userId, Guid playerId)
    {
        return $"player:user:{userId:N}:player:{playerId:N}:v1";
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

    #region Admin

    public async Task<AdminPageDto<AdminPlayerListItemDto>> ListPlayersAsync(
        int page,
        int pageSize,
        Guid? ownerId,
        string? search,
        CancellationToken cancellationToken)
    {
        const int maxPageSize = 100;
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, maxPageSize);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.Players
            .AsNoTracking()
            .Include(player => player.User)
            .AsQueryable();

        if (ownerId is not null)
        {
            query = query.Where(player => player.UserId == ownerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = YggdrasilFormat.Normalize(search);
            query = query.Where(player =>
                player.NormalizedName.Contains(normalized)
                || player.User.NormalizedUserName.Contains(normalized)
                || player.User.NormalizedEmail.Contains(normalized));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (totalCount + pageSize - 1) / pageSize;
        if (totalPages > 0)
        {
            page = Math.Min(page, totalPages);
        }

        var players = await query
            .OrderByDescending(player => player.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        var items = players.Select(player => mapper.Map<AdminPlayerListItemDto>(player, options =>
        {
            options.Items[AdminMappingContextKeys.OwnerUserName] = player.User?.UserName ?? string.Empty;
        })).ToArray();

        return new AdminPageDto<AdminPlayerListItemDto>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<AdminPlayerDetailDto> GetPlayerDetailAsync(Guid playerId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var player = await dbContext.Players
            .AsNoTracking()
            .Include(entity => entity.User)
            .Include(entity => entity.SkinTexture)
            .Include(entity => entity.CapeTexture)
            .FirstOrDefaultAsync(entity => entity.Id == playerId, cancellationToken);
        if (player is null)
        {
            throw new PlayerException(StatusCodes.Status404NotFound, "Player was not found.");
        }

        return mapper.Map<AdminPlayerDetailDto>(player, options =>
        {
            options.Items[AdminMappingContextKeys.OwnerUserName] = player.User?.UserName ?? string.Empty;
        });
    }

    public async Task<AdminPlayerDetailDto> UpdatePlayerAsync(
        Guid playerId,
        AdminUpdatePlayerRequest request,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var player = await dbContext.Players.FirstOrDefaultAsync(entity => entity.Id == playerId, cancellationToken);
        if (player is null)
        {
            throw new PlayerException(StatusCodes.Status404NotFound, "Player was not found.");
        }

        var originalOwnerId = player.UserId;
        var originalName = player.Name;

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            ValidatePlayerName(name);
            var normalizedName = YggdrasilFormat.Normalize(name);
            var nameExists = await dbContext.Players.AnyAsync(
                entity => entity.Id != playerId && entity.NormalizedName == normalizedName,
                cancellationToken);
            if (nameExists)
            {
                throw new PlayerException(StatusCodes.Status409Conflict, "Player name already exists.");
            }

            player.Name = name;
            player.NormalizedName = normalizedName;
        }

        if (request.SkinModel is not null)
        {
            player.SkinModel = request.SkinModel.Value;
        }

        if (request.ClearSkinTexture)
        {
            player.SkinTextureId = null;
        }
        else if (request.SkinTextureId is not null)
        {
            await EnsureAdminTextureTypeAsync(dbContext, request.SkinTextureId.Value, TextureType.Skin, cancellationToken);
            player.SkinTextureId = request.SkinTextureId;
        }

        if (request.ClearCapeTexture)
        {
            player.CapeTextureId = null;
        }
        else if (request.CapeTextureId is not null)
        {
            await EnsureAdminTextureTypeAsync(dbContext, request.CapeTextureId.Value, TextureType.Cape, cancellationToken);
            player.CapeTextureId = request.CapeTextureId;
        }

        if (request.UserId is not null && request.UserId.Value != player.UserId)
        {
            var targetUserId = request.UserId.Value;
            var targetUser = await dbContext.Users
                .AsNoTracking()
                .Include(entity => entity.Players)
                .FirstOrDefaultAsync(entity => entity.Id == targetUserId, cancellationToken);
            if (targetUser is null)
            {
                throw new PlayerException(StatusCodes.Status404NotFound, "Target user was not found.");
            }

            if (targetUser.IsDisabled)
            {
                throw new PlayerException(StatusCodes.Status400BadRequest, "Target user is disabled.");
            }

            if (targetUser.Players.Count >= targetUser.MaxPlayerCount)
            {
                throw new PlayerException(StatusCodes.Status409Conflict, "Target user's player limit has been reached.");
            }

            player.UserId = targetUserId;
        }

        var ownerChanged = player.UserId != originalOwnerId;
        var nameChanged = player.Name != originalName;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            if (ownerChanged)
            {
                await yggdrasilLifecycle.RevokePlayerClientsAsync(dbContext, playerId, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await RemovePlayerCachesAsync(originalOwnerId, playerId, cancellationToken);
        if (ownerChanged)
        {
            await RemovePlayerCachesAsync(player.UserId, playerId, cancellationToken);
        }

        if (nameChanged)
        {
            await yggdrasilLifecycle.MarkPlayerRenamedAsync(player.MinecraftUuid, cancellationToken);
        }

        await unionService.MarkProfilesDirtyAsync(cancellationToken);

        return await GetPlayerDetailAsync(playerId, cancellationToken);
    }

    public async Task DeletePlayerAdminAsync(Guid playerId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var player = await dbContext.Players.FirstOrDefaultAsync(entity => entity.Id == playerId, cancellationToken);
        if (player is null)
        {
            throw new PlayerException(StatusCodes.Status404NotFound, "Player was not found.");
        }

        var ownerId = player.UserId;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            await yggdrasilLifecycle.RevokePlayerClientsAsync(dbContext, playerId, cancellationToken);
            dbContext.Players.Remove(player);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        await RemovePlayerCachesAsync(ownerId, playerId, cancellationToken);
        await unionService.MarkProfilesDirtyAsync(cancellationToken);
    }

    public async Task<int> CountPlayersAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Players.CountAsync(cancellationToken);
    }

    private static async Task EnsureAdminTextureTypeAsync(
        AppDbContext dbContext,
        Guid textureId,
        TextureType textureType,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.Textures
            .AsNoTracking()
            .AnyAsync(entity => entity.Id == textureId && entity.Type == textureType, cancellationToken);
        if (!exists)
        {
            throw new PlayerException(StatusCodes.Status404NotFound, $"{textureType} texture was not found.");
        }
    }

    #endregion
}
