using System.Buffers.Binary;
using System.Security.Cryptography;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Entity;
using Renatus.Host.Service.Cache;
using Renatus.Host.Utils;

namespace Renatus.Host.Service;

public sealed class TextureService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    ICacheService cacheService,
    IMapper mapper)
{
    public async Task<TextureDto> UploadTextureAsync(
        Guid userId,
        UploadTextureRequest request,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureUserAvailableAsync(dbContext, userId, cancellationToken);
        if (request.Type is null)
        {
            throw new TextureException(StatusCodes.Status400BadRequest, "Texture type is required.");
        }

        var name = request.Name.Trim();
        if (name.Length is < 1 or > 80)
        {
            throw new TextureException(StatusCodes.Status400BadRequest, "Texture name must be 1-80 characters.");
        }

        var descriptionMarkdown = request.DescriptionMarkdown?.Trim() ?? string.Empty;
        if (descriptionMarkdown.Length > 4000)
        {
            throw new TextureException(StatusCodes.Status400BadRequest, "Texture description must be 4000 characters or fewer.");
        }

        var bytes = DecodeBase64Image(request.DataBase64);
        var (width, height) = ReadPngSize(bytes);
        var textureType = request.Type.Value;
        EnsureTextureSize(textureType, width, height);

        var texture = new Texture
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = textureType,
            Name = name,
            Source = request.Source,
            DescriptionMarkdown = descriptionMarkdown,
            IsPublic = request.IsPublic,
            SkinModel = textureType == TextureType.Skin ? request.SkinModel : SkinModel.Default,
            DataBase64 = Convert.ToBase64String(bytes),
            ContentType = "image/png",
            Width = width,
            Height = height,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
        };

        dbContext.Textures.Add(texture);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (texture.IsPublic)
        {
            await cacheService.RemoveAsync(TextureLibraryService.PublicTextureCountCacheKey(), cancellationToken);
        }

        return ToDto(texture);
    }

    public async Task<UserTexturesResponse> GetUserTexturesAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureUserAvailableAsync(dbContext, userId, cancellationToken);

        var textures = await dbContext.Textures
            .AsNoTracking()
            .Where(texture => texture.UserId == userId)
            .OrderByDescending(texture => texture.CreatedAt)
            .Select(texture => ToDto(texture))
            .ToArrayAsync(cancellationToken);

        return new UserTexturesResponse(textures);
    }

    public async Task<TextureDto> DeleteTextureAsync(Guid userId, Guid textureId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var texture = await dbContext.Textures
            .FirstOrDefaultAsync(entity => entity.Id == textureId && entity.UserId == userId, cancellationToken);
        if (texture is null)
        {
            throw new TextureException(StatusCodes.Status404NotFound, "Texture was not found.");
        }

        var affectedUserIds = await ClearPlayerTextureReferencesAsync(dbContext, texture, cancellationToken);
        dbContext.Textures.Remove(texture);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var affectedUserId in affectedUserIds)
        {
            await cacheService.RemoveAsync(PlayerService.UserPlayersCacheKey(affectedUserId), cancellationToken);
        }

        await cacheService.RemoveAsync(TextureLibraryService.PublicTextureCountCacheKey(), cancellationToken);
        await cacheService.RemoveAsync(TextureLibraryService.PublicTextureCacheKey(textureId), cancellationToken);
        await cacheService.RemoveAsync(TextureLibraryService.PublicTexturePreviewCacheKey(textureId), cancellationToken);
        await cacheService.RemoveAsync(TextureLibraryService.UserFavoriteIdsCacheKey(userId), cancellationToken);
        await cacheService.RemoveAsync(TextureLibraryService.UserFavoriteTexturesCacheKey(userId), cancellationToken);

        return ToDto(texture);
    }

    private async Task<Guid[]> ClearPlayerTextureReferencesAsync(
        AppDbContext dbContext,
        Texture texture,
        CancellationToken cancellationToken)
    {
        if (texture.Type == TextureType.Skin)
        {
            var affectedUserIds = await dbContext.Players
                .Where(player => player.SkinTextureId == texture.Id)
                .Select(player => player.UserId)
                .Distinct()
                .ToArrayAsync(cancellationToken);

            await dbContext.Players
                .Where(player => player.SkinTextureId == texture.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(player => player.SkinTextureId, player => (Guid?)null),
                    cancellationToken);

            return affectedUserIds;
        }

        var capeAffectedUserIds = await dbContext.Players
            .Where(player => player.CapeTextureId == texture.Id)
            .Select(player => player.UserId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        await dbContext.Players
            .Where(player => player.CapeTextureId == texture.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(player => player.CapeTextureId, player => (Guid?)null),
                cancellationToken);

        return capeAffectedUserIds;
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

    private static byte[] DecodeBase64Image(string dataBase64)
    {
        var value = dataBase64.Trim();
        var commaIndex = value.IndexOf(',');
        if (commaIndex >= 0)
        {
            value = value[(commaIndex + 1)..];
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            throw new TextureException(StatusCodes.Status400BadRequest, "Texture image must be valid base64.");
        }
    }

    private static (int Width, int Height) ReadPngSize(byte[] bytes)
    {
        ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 24 || !bytes.AsSpan(0, pngSignature.Length).SequenceEqual(pngSignature))
        {
            throw new TextureException(StatusCodes.Status400BadRequest, "Texture image must be a PNG file.");
        }

        var chunkType = bytes.AsSpan(12, 4);
        ReadOnlySpan<byte> ihdr = [(byte)'I', (byte)'H', (byte)'D', (byte)'R'];
        if (!chunkType.SequenceEqual(ihdr))
        {
            throw new TextureException(StatusCodes.Status400BadRequest, "Texture PNG header is invalid.");
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        if (width <= 0 || height <= 0)
        {
            throw new TextureException(StatusCodes.Status400BadRequest, "Texture PNG size is invalid.");
        }

        return (width, height);
    }

    private static void EnsureTextureSize(TextureType type, int width, int height)
    {
        if (type == TextureType.Skin && (IsMultipleOf(width, height, 64, 64) || IsMultipleOf(width, height, 64, 32)))
        {
            return;
        }

        if (type == TextureType.Cape && IsMultipleOf(width, height, 64, 32))
        {
            return;
        }

        throw new TextureException(StatusCodes.Status400BadRequest, "Texture PNG size is not allowed.");
    }

    private static bool IsMultipleOf(int width, int height, int baseWidth, int baseHeight)
    {
        var widthMultiple = width / baseWidth;
        return widthMultiple > 0
            && width % baseWidth == 0
            && height == baseHeight * widthMultiple;
    }

    private static TextureDto ToDto(Texture texture)
    {
        return new TextureDto(
            texture.Id,
            texture.Name,
            texture.Type,
            texture.Source,
            texture.DescriptionMarkdown,
            texture.IsPublic,
            texture.SkinModel,
            texture.Width,
            texture.Height,
            texture.Sha256,
            texture.CreatedAt);
    }

    #region Admin

    public async Task<AdminPageDto<AdminTextureListItemDto>> ListTexturesAsync(
        int page,
        int pageSize,
        bool? isPublic,
        TextureType? type,
        Guid? uploaderId,
        TextureSource? source,
        string? sha256,
        CancellationToken cancellationToken)
    {
        const int maxPageSize = 100;
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, maxPageSize);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.Textures
            .AsNoTracking()
            .Include(texture => texture.User)
            .AsQueryable();

        if (isPublic is not null)
        {
            query = query.Where(texture => texture.IsPublic == isPublic.Value);
        }

        if (type is not null)
        {
            query = query.Where(texture => texture.Type == type.Value);
        }

        if (uploaderId is not null)
        {
            query = query.Where(texture => texture.UserId == uploaderId.Value);
        }

        if (source is not null)
        {
            query = query.Where(texture => texture.Source == source.Value);
        }

        if (!string.IsNullOrWhiteSpace(sha256))
        {
            var normalizedHash = sha256.Trim().ToLowerInvariant();
            query = query.Where(texture => texture.Sha256 != null && texture.Sha256 == normalizedHash);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (totalCount + pageSize - 1) / pageSize;
        if (totalPages > 0)
        {
            page = Math.Min(page, totalPages);
        }

        var textures = await query
            .OrderByDescending(texture => texture.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        var items = textures.Select(texture => mapper.Map<AdminTextureListItemDto>(texture, options =>
        {
            options.Items[AdminMappingContextKeys.UploaderId] = texture.UserId;
            options.Items[AdminMappingContextKeys.UploaderName] = UploaderDisplayName(texture);
        })).ToArray();

        return new AdminPageDto<AdminTextureListItemDto>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<AdminTextureDetailDto> GetTextureDetailAsync(Guid textureId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var texture = await dbContext.Textures
            .AsNoTracking()
            .Include(entity => entity.User)
            .FirstOrDefaultAsync(entity => entity.Id == textureId, cancellationToken);
        if (texture is null)
        {
            throw new TextureException(StatusCodes.Status404NotFound, "Texture was not found.");
        }

        return mapper.Map<AdminTextureDetailDto>(texture, options =>
        {
            options.Items[AdminMappingContextKeys.UploaderId] = texture.UserId;
            options.Items[AdminMappingContextKeys.UploaderName] = UploaderDisplayName(texture);
        });
    }

    public async Task<AdminTextureDetailDto> UpdateTextureAsync(
        Guid textureId,
        AdminUpdateTextureRequest request,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var texture = await dbContext.Textures.FirstOrDefaultAsync(entity => entity.Id == textureId, cancellationToken);
        if (texture is null)
        {
            throw new TextureException(StatusCodes.Status404NotFound, "Texture was not found.");
        }

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length is < 1 or > 80)
            {
                throw new TextureException(StatusCodes.Status400BadRequest, "Texture name must be 1-80 characters.");
            }

            texture.Name = name;
        }

        if (request.DescriptionMarkdown is not null)
        {
            var description = request.DescriptionMarkdown.Trim();
            if (description.Length > 4000)
            {
                throw new TextureException(StatusCodes.Status400BadRequest, "Texture description must be 4000 characters or fewer.");
            }

            texture.DescriptionMarkdown = description;
        }

        if (request.IsPublic is not null)
        {
            texture.IsPublic = request.IsPublic.Value;
        }

        if (request.Source is not null)
        {
            texture.Source = request.Source.Value;
        }

        if (request.SkinModel is not null && texture.Type == TextureType.Skin)
        {
            texture.SkinModel = request.SkinModel.Value;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await cacheService.RemoveAsync(TextureLibraryService.PublicTextureCountCacheKey(), cancellationToken);
        await cacheService.RemoveAsync(TextureLibraryService.PublicTextureCacheKey(textureId), cancellationToken);
        await cacheService.RemoveAsync(TextureLibraryService.PublicTexturePreviewCacheKey(textureId), cancellationToken);

        return await GetTextureDetailAsync(textureId, cancellationToken);
    }

    public async Task DeleteTextureAdminAsync(Guid textureId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var texture = await dbContext.Textures.FirstOrDefaultAsync(entity => entity.Id == textureId, cancellationToken);
        if (texture is null)
        {
            throw new TextureException(StatusCodes.Status404NotFound, "Texture was not found.");
        }

        var affectedUserIds = await ClearPlayerTextureReferencesAsync(dbContext, texture, cancellationToken);
        dbContext.Textures.Remove(texture);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var affectedUserId in affectedUserIds)
        {
            await cacheService.RemoveAsync(PlayerService.UserPlayersCacheKey(affectedUserId), cancellationToken);
        }

        if (texture.UserId is not null)
        {
            await cacheService.RemoveAsync(TextureLibraryService.UserFavoriteIdsCacheKey(texture.UserId.Value), cancellationToken);
            await cacheService.RemoveAsync(TextureLibraryService.UserFavoriteTexturesCacheKey(texture.UserId.Value), cancellationToken);
        }

        await cacheService.RemoveAsync(TextureLibraryService.PublicTextureCountCacheKey(), cancellationToken);
        await cacheService.RemoveAsync(TextureLibraryService.PublicTextureCacheKey(textureId), cancellationToken);
        await cacheService.RemoveAsync(TextureLibraryService.PublicTexturePreviewCacheKey(textureId), cancellationToken);
    }

    public async Task<int> CountTexturesAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Textures.CountAsync(cancellationToken);
    }

    private static string UploaderDisplayName(Texture texture)
    {
        if (texture.User is null)
        {
            return "Renatus";
        }

        return string.IsNullOrWhiteSpace(texture.User.NickName) ? texture.User.UserName : texture.User.NickName;
    }

    #endregion
}
