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
    SkinModel SkinModel = SkinModel.Default);

public sealed record TextureDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("type")] TextureType Type,
    [property: JsonPropertyName("isPublic")] bool IsPublic,
    [property: JsonPropertyName("skinModel")] SkinModel SkinModel,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("sha256")] string? Sha256,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

public sealed record UserTexturesResponse(
    [property: JsonPropertyName("textures")] IReadOnlyCollection<TextureDto> Textures);
