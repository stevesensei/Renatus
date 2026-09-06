using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Entity;
using Renatus.Host.Service.Cache;
using Renatus.Host.Utils;

namespace Renatus.Host.Service;

public sealed class TextureService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    ICacheService cacheService)
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

        var bytes = DecodeBase64Image(request.DataBase64);
        var (width, height) = ReadPngSize(bytes);
        var textureType = request.Type.Value;
        EnsureTextureSize(textureType, width, height);

        var texture = new Texture
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = textureType,
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
            texture.Type,
            texture.IsPublic,
            texture.SkinModel,
            texture.Width,
            texture.Height,
            texture.Sha256,
            texture.CreatedAt);
    }
}
