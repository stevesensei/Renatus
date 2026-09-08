using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Entity;
using Renatus.Host.Service.Cache;
using Renatus.Host.Utils;

namespace Renatus.Host.Service;

public sealed class TextureLibraryService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    ICacheService cacheService)
{
    private static readonly TimeSpan PublicPageCacheTtl = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PublicTextureCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FavoriteIdsCacheTtl = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FavoriteTexturesCacheTtl = TimeSpan.FromMinutes(5);

    public async Task<PublicTexturePageResponse> GetPublicTexturesAsync(
        int page,
        int pageSize,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        const int maxPageSize = 24;
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, maxPageSize);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var totalCount = await cacheService.GetAsync<int?>(PublicTextureCountCacheKey(), cancellationToken);
        if (totalCount is null)
        {
            totalCount = await dbContext.Textures
                .AsNoTracking()
                .CountAsync(texture => texture.IsPublic, cancellationToken);
            await cacheService.SetAsync(PublicTextureCountCacheKey(), totalCount.Value, PublicPageCacheTtl, cancellationToken);
        }

        var totalCountValue = totalCount.Value;
        var totalPages = totalCountValue == 0 ? 0 : (totalCountValue + pageSize - 1) / pageSize;
        if (totalPages > 0)
        {
            page = Math.Min(page, totalPages);
        }

        var pageKey = PublicTexturePageIdsCacheKey(page, pageSize);
        var pageTextureIds = await cacheService.GetAsync<Guid[]>(pageKey, cancellationToken);
        if (pageTextureIds is null)
        {
            pageTextureIds = await dbContext.Textures
                .AsNoTracking()
                .Where(texture => texture.IsPublic)
                .OrderByDescending(texture => texture.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(texture => texture.Id)
                .ToArrayAsync(cancellationToken);
            await cacheService.SetAsync(pageKey, pageTextureIds, PublicPageCacheTtl, cancellationToken);
        }

        var favoriteIds = await GetFavoriteTextureIdsAsync(dbContext, userId, cancellationToken);
        var cachedTextures = await Task.WhenAll(
            pageTextureIds.Select(async textureId =>
                (textureId, texture: await cacheService.GetAsync<PublicTextureDto>(
                    PublicTextureCacheKey(textureId),
                    cancellationToken))));
        var texturesById = cachedTextures
            .Where(item => item.texture is not null)
            .ToDictionary(item => item.textureId, item => item.texture!);

        foreach (var textureId in pageTextureIds.Where(textureId => !texturesById.ContainsKey(textureId)))
        {
            var texture = await dbContext.Textures
                .AsNoTracking()
                .Include(entity => entity.User)
                .FirstOrDefaultAsync(entity => entity.Id == textureId && entity.IsPublic, cancellationToken);
            if (texture is null)
            {
                continue;
            }

            var publicTexture = ToPublicDto(texture);
            texturesById[textureId] = publicTexture;
            await cacheService.SetAsync(
                PublicTextureCacheKey(textureId),
                publicTexture,
                PublicTextureCacheTtl,
                cancellationToken);
        }

        var textures = pageTextureIds
            .Where(texturesById.ContainsKey)
            .Select(textureId => texturesById[textureId] with { IsFavorited = favoriteIds.Contains(textureId) })
            .ToArray();

        return new PublicTexturePageResponse(textures, page, pageSize, totalCountValue, totalPages);
    }

    public async Task<TexturePreviewDto?> GetPublicPreviewAsync(
        Guid textureId,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var preview = await cacheService.GetAsync<TexturePreviewDto>(
            PublicTexturePreviewCacheKey(textureId),
            cancellationToken);
        if (preview is null)
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var texture = await dbContext.Textures
                .AsNoTracking()
                .Include(entity => entity.User)
                .FirstOrDefaultAsync(
                    entity => entity.Id == textureId && entity.IsPublic,
                    cancellationToken);
            if (texture is null)
            {
                return null;
            }

            preview = ToPreviewDto(texture);
            await cacheService.SetAsync(
                PublicTexturePreviewCacheKey(textureId),
                preview,
                PublicTextureCacheTtl,
                cancellationToken);
        }

        var favoriteIds = await GetFavoriteTextureIdsAsync(null, userId, cancellationToken);
        return preview with { IsFavorited = userId is not null && favoriteIds.Contains(textureId) };
    }

    public async Task<FavoriteTextureResponse> FavoriteTextureAsync(
        Guid userId,
        Guid textureId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureUserAvailableAsync(dbContext, userId, cancellationToken);

        var texture = await dbContext.Textures
            .AsNoTracking()
            .FirstOrDefaultAsync(
                entity => entity.Id == textureId && entity.IsPublic,
                cancellationToken);
        if (texture is null)
        {
            throw new TextureException(StatusCodes.Status404NotFound, "Texture was not found.");
        }

        var exists = await dbContext.UserTextureFavorites
            .AnyAsync(favorite => favorite.UserId == userId && favorite.TextureId == textureId, cancellationToken);
        if (!exists)
        {
            dbContext.UserTextureFavorites.Add(new UserTextureFavorite
            {
                UserId = userId,
                TextureId = textureId
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await RemoveFavoriteCachesAsync(userId, cancellationToken);

        return new FavoriteTextureResponse(textureId, true);
    }

    public async Task<FavoriteTextureResponse> UnfavoriteTextureAsync(
        Guid userId,
        Guid textureId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureUserAvailableAsync(dbContext, userId, cancellationToken);

        var favorite = await dbContext.UserTextureFavorites
            .Include(entity => entity.Texture)
            .FirstOrDefaultAsync(
                entity => entity.UserId == userId && entity.TextureId == textureId,
                cancellationToken);
        if (favorite is null)
        {
            await RemoveFavoriteCachesAsync(userId, cancellationToken);
            return new FavoriteTextureResponse(textureId, false);
        }

        if (favorite.Texture.UserId != userId)
        {
            dbContext.UserTextureFavorites.Remove(favorite);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await RemoveFavoriteCachesAsync(userId, cancellationToken);

        return new FavoriteTextureResponse(textureId, favorite.Texture.UserId == userId);
    }

    public async Task<UserFavoriteTexturesResponse> GetUserFavoriteTexturesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureUserAvailableAsync(dbContext, userId, cancellationToken);

        var cacheKey = UserFavoriteTexturesCacheKey(userId);
        var cached = await cacheService.GetAsync<UserFavoriteTexturesResponse>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var textures = await dbContext.UserTextureFavorites
            .AsNoTracking()
            .Where(favorite => favorite.UserId == userId)
            .OrderByDescending(favorite => favorite.CreatedAt)
            .Select(favorite => new TextureDto(
                favorite.Texture.Id,
                favorite.Texture.Name,
                favorite.Texture.Type,
                favorite.Texture.Source,
                favorite.Texture.DescriptionMarkdown,
                favorite.Texture.IsPublic,
                favorite.Texture.SkinModel,
                favorite.Texture.Width,
                favorite.Texture.Height,
                favorite.Texture.Sha256,
                favorite.Texture.CreatedAt))
            .ToArrayAsync(cancellationToken);

        var response = new UserFavoriteTexturesResponse(textures);
        await cacheService.SetAsync(cacheKey, response, FavoriteTexturesCacheTtl, cancellationToken);
        return response;
    }

    private async Task<HashSet<Guid>> GetFavoriteTextureIdsAsync(
        AppDbContext? dbContext,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return [];
        }

        var cacheKey = UserFavoriteIdsCacheKey(userId.Value);
        var cached = await cacheService.GetAsync<Guid[]>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return [.. cached];
        }

        var ownsDbContext = dbContext is null;
        var context = dbContext ?? await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var ids = await context.UserTextureFavorites
            .AsNoTracking()
            .Where(favorite => favorite.UserId == userId.Value)
            .Select(favorite => favorite.TextureId)
            .ToArrayAsync(cancellationToken);
        await cacheService.SetAsync(cacheKey, ids, FavoriteIdsCacheTtl, cancellationToken);
        if (ownsDbContext)
        {
            await context.DisposeAsync();
        }

        return [.. ids];
    }

    private async Task RemoveFavoriteCachesAsync(Guid userId, CancellationToken cancellationToken)
    {
        await cacheService.RemoveAsync(UserFavoriteIdsCacheKey(userId), cancellationToken);
        await cacheService.RemoveAsync(UserFavoriteTexturesCacheKey(userId), cancellationToken);
    }

    private static PublicTextureDto ToPublicDto(Texture texture)
    {
        return new PublicTextureDto(
            texture.Id,
            texture.Name,
            texture.Type,
            texture.Source,
            texture.SkinModel,
            texture.Width,
            texture.Height,
            GetUploaderName(texture),
            texture.CreatedAt,
            false);
    }

    private static TexturePreviewDto ToPreviewDto(Texture texture)
    {
        var bytes = Convert.FromBase64String(texture.DataBase64);
        return new TexturePreviewDto(
            texture.Id,
            texture.Name,
            texture.Source,
            texture.Type,
            texture.SkinModel,
            texture.Width,
            texture.Height,
            bytes.Length,
            $"data:{texture.ContentType};base64,{texture.DataBase64}",
            GetUploaderName(texture),
            texture.DescriptionMarkdown,
            texture.CreatedAt,
            false);
    }

    private static string GetUploaderName(Texture texture)
    {
        return texture.User is null
            ? "Renatus"
            : string.IsNullOrWhiteSpace(texture.User.NickName) ? texture.User.UserName : texture.User.NickName;
    }

    public static string PublicTexturePageIdsCacheKey(int page, int pageSize) =>
        $"texture:public:page:{page}:size:{pageSize}:ids:v1";

    public static string PublicTextureCountCacheKey() => "texture:public:count:v1";

    public static string PublicTextureCacheKey(Guid textureId) =>
        $"texture:public:item:{textureId:N}:v1";

    public static string PublicTexturePreviewCacheKey(Guid textureId) =>
        $"texture:public:preview:{textureId:N}:v1";

    public static string UserFavoriteIdsCacheKey(Guid userId) =>
        $"texture:user:{userId:N}:favorite-ids:v1";

    public static string UserFavoriteTexturesCacheKey(Guid userId) =>
        $"texture:user:{userId:N}:favorites:v1";

    private static async Task EnsureUserAvailableAsync(
        AppDbContext dbContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(entity => entity.Id == userId && !entity.IsDisabled, cancellationToken);
        if (!exists)
        {
            throw new TextureException(StatusCodes.Status401Unauthorized, "User is unavailable.");
        }
    }
}
