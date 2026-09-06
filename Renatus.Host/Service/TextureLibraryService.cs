using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Entity;
using Renatus.Host.Utils;

namespace Renatus.Host.Service;

public sealed class TextureLibraryService(IDbContextFactory<AppDbContext> dbContextFactory)
{
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
        var query = dbContext.Textures
            .AsNoTracking()
            .Where(texture => texture.Type == TextureType.Skin && texture.IsPublic)
            .OrderByDescending(texture => texture.CreatedAt);
        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (totalCount + pageSize - 1) / pageSize;
        if (totalPages > 0)
        {
            page = Math.Min(page, totalPages);
        }

        var favoriteIds = await GetFavoriteTextureIdsAsync(dbContext, userId, cancellationToken);
        var textures = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(texture => new PublicTextureDto(
                texture.Id,
                texture.Name,
                texture.Source,
                texture.SkinModel,
                texture.Width,
                texture.Height,
                texture.User == null ? "Renatus" : texture.User.NickName,
                texture.CreatedAt,
                favoriteIds.Contains(texture.Id)))
            .ToArrayAsync(cancellationToken);

        return new PublicTexturePageResponse(textures, page, pageSize, totalCount, totalPages);
    }

    public async Task<TexturePreviewDto?> GetPublicPreviewAsync(
        Guid textureId,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var texture = await dbContext.Textures
            .AsNoTracking()
            .Include(entity => entity.User)
            .FirstOrDefaultAsync(
                entity => entity.Id == textureId && entity.Type == TextureType.Skin && entity.IsPublic,
                cancellationToken);
        if (texture is null)
        {
            return null;
        }

        var isFavorited = userId is not null
            && await dbContext.UserTextureFavorites
                .AsNoTracking()
                .AnyAsync(
                    favorite => favorite.UserId == userId && favorite.TextureId == textureId,
                    cancellationToken);

        var uploader = texture.User is null
            ? "Renatus"
            : string.IsNullOrWhiteSpace(texture.User.NickName) ? texture.User.UserName : texture.User.NickName;
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
            uploader,
            texture.DescriptionMarkdown,
            texture.CreatedAt,
            isFavorited);
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
                entity => entity.Id == textureId && entity.Type == TextureType.Skin && entity.IsPublic,
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
            .FirstOrDefaultAsync(
                entity => entity.UserId == userId && entity.TextureId == textureId,
                cancellationToken);
        if (favorite is not null)
        {
            dbContext.UserTextureFavorites.Remove(favorite);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new FavoriteTextureResponse(textureId, false);
    }

    public async Task<UserFavoriteTexturesResponse> GetUserFavoriteTexturesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureUserAvailableAsync(dbContext, userId, cancellationToken);

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

        return new UserFavoriteTexturesResponse(textures);
    }

    private static async Task<HashSet<Guid>> GetFavoriteTextureIdsAsync(
        AppDbContext dbContext,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return [];
        }

        var ids = await dbContext.UserTextureFavorites
            .AsNoTracking()
            .Where(favorite => favorite.UserId == userId)
            .Select(favorite => favorite.TextureId)
            .ToArrayAsync(cancellationToken);
        return [.. ids];
    }

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
