using System.Text.Json.Serialization;

namespace Renatus.Host.Frontend.Models;

public sealed record CreatePlayerRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("skinModel")] SkinModel SkinModel = SkinModel.Default,
    [property: JsonPropertyName("skinTextureId")] Guid? SkinTextureId = null,
    [property: JsonPropertyName("capeTextureId")] Guid? CapeTextureId = null);

public sealed record UpdatePlayerTexturesRequest(
    [property: JsonPropertyName("skinTextureId")] Guid? SkinTextureId,
    [property: JsonPropertyName("capeTextureId")] Guid? CapeTextureId,
    [property: JsonPropertyName("skinModel")] SkinModel? SkinModel = null);

public sealed record PlayerDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("skinModel")] SkinModel SkinModel,
    [property: JsonPropertyName("skinTextureId")] Guid? SkinTextureId,
    [property: JsonPropertyName("capeTextureId")] Guid? CapeTextureId,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);

public sealed record UserPlayersResponse(
    [property: JsonPropertyName("players")] IReadOnlyCollection<PlayerDto> Players,
    [property: JsonPropertyName("maxPlayerCount")] int MaxPlayerCount);

public sealed record DeletePlayerResponse([property: JsonPropertyName("id")] Guid Id);
