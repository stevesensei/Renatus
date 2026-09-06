using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Entity;

namespace Renatus.Host.Service;

public sealed class TextureLibraryService(IDbContextFactory<AppDbContext> dbContextFactory)
{
    public async Task<PublicTexturePageResponse> GetPublicTexturesAsync(
        int page,
        int pageSize,
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
                texture.CreatedAt))
            .ToArrayAsync(cancellationToken);

        return new PublicTexturePageResponse(textures, page, pageSize, totalCount, totalPages);
    }

    public async Task<TexturePreviewDto?> GetPublicPreviewAsync(Guid textureId, CancellationToken cancellationToken)
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
            texture.CreatedAt);
    }
}
