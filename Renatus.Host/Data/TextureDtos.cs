using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Renatus.Host.Entity;

namespace Renatus.Host.Data;

public sealed record UploadTextureRequest(
    [property: Required]
    [property: JsonPropertyName("type")]
    TextureType? Type,

    [property: Required]
    [property: JsonPropertyName("dataBase64")]
    string DataBase64,

    [property: JsonPropertyName("isPublic")]
    bool IsPublic,

    [property: JsonPropertyName("skinModel")]
    SkinModel SkinModel = SkinModel.Default,

    [property: JsonPropertyName("name")]
    string Name = "未命名材质",

    [property: JsonPropertyName("source")]
    TextureSource Source = TextureSource.Original,

    [property: JsonPropertyName("descriptionMarkdown")]
    string? DescriptionMarkdown = null);

public sealed record TextureDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] TextureType Type,
    [property: JsonPropertyName("source")] TextureSource Source,
    [property: JsonPropertyName("descriptionMarkdown")] string DescriptionMarkdown,
    [property: JsonPropertyName("isPublic")] bool IsPublic,
    [property: JsonPropertyName("skinModel")] SkinModel SkinModel,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("sha256")] string? Sha256,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

public sealed record UserTexturesResponse(
    [property: JsonPropertyName("textures")] IReadOnlyCollection<TextureDto> Textures);

public sealed record PublicTextureDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] TextureType Type,
    [property: JsonPropertyName("source")] TextureSource Source,
    [property: JsonPropertyName("skinModel")] SkinModel SkinModel,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("uploader")] string Uploader,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("isFavorited")] bool IsFavorited);

public sealed record PublicTexturePageResponse(
    [property: JsonPropertyName("textures")] IReadOnlyCollection<PublicTextureDto> Textures,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("totalCount")] int TotalCount,
    [property: JsonPropertyName("totalPages")] int TotalPages);

public sealed record TexturePreviewDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("source")] TextureSource Source,
    [property: JsonPropertyName("type")] TextureType Type,
    [property: JsonPropertyName("skinModel")] SkinModel SkinModel,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("sizeBytes")] int SizeBytes,
    [property: JsonPropertyName("dataUrl")] string DataUrl,
    [property: JsonPropertyName("uploader")] string Uploader,
    [property: JsonPropertyName("descriptionMarkdown")] string DescriptionMarkdown,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("isFavorited")] bool IsFavorited);

public sealed record FavoriteTextureRequest(
    [property: Required]
    [property: JsonPropertyName("textureId")]
    Guid TextureId);

public sealed record FavoriteTextureResponse(
    [property: JsonPropertyName("textureId")] Guid TextureId,
    [property: JsonPropertyName("isFavorited")] bool IsFavorited);

public sealed record UserFavoriteTexturesResponse(
    [property: JsonPropertyName("textures")] IReadOnlyCollection<TextureDto> Textures);
