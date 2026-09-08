using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Renatus.Host.Entity;

namespace Renatus.Host.Data;

public sealed record AdminPageDto<T>(
    [property: JsonPropertyName("items")] IReadOnlyCollection<T> Items,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("totalCount")] int TotalCount,
    [property: JsonPropertyName("totalPages")] int TotalPages);

public sealed record AdminUserListItemDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("nickName")] string NickName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("preferredLanguage")] string PreferredLanguage,
    [property: JsonPropertyName("role")] UserRole Role,
    [property: JsonPropertyName("maxPlayerCount")] int MaxPlayerCount,
    [property: JsonPropertyName("isDisabled")] bool IsDisabled,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);

public sealed record AdminUserDetailDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("nickName")] string NickName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("preferredLanguage")] string PreferredLanguage,
    [property: JsonPropertyName("role")] UserRole Role,
    [property: JsonPropertyName("maxPlayerCount")] int MaxPlayerCount,
    [property: JsonPropertyName("isDisabled")] bool IsDisabled,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("players")] IReadOnlyCollection<AdminPlayerListItemDto> Players,
    [property: JsonPropertyName("textures")] IReadOnlyCollection<AdminTextureListItemDto> Textures,
    [property: JsonPropertyName("sessions")] IReadOnlyCollection<AdminSessionDto> Sessions);

public sealed record AdminPlayerListItemDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("skinModel")] SkinModel SkinModel,
    [property: JsonPropertyName("skinTextureId")] Guid? SkinTextureId,
    [property: JsonPropertyName("capeTextureId")] Guid? CapeTextureId,
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);

public sealed record AdminPlayerDetailDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("skinModel")] SkinModel SkinModel,
    [property: JsonPropertyName("skinTextureId")] Guid? SkinTextureId,
    [property: JsonPropertyName("skinTexture")] AdminTextureRefDto? SkinTexture,
    [property: JsonPropertyName("capeTextureId")] Guid? CapeTextureId,
    [property: JsonPropertyName("capeTexture")] AdminTextureRefDto? CapeTexture,
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);

public sealed record AdminTextureRefDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] TextureType Type,
    [property: JsonPropertyName("isPublic")] bool IsPublic);

public sealed record AdminTextureListItemDto(
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
    [property: JsonPropertyName("uploaderId")] Guid? UploaderId,
    [property: JsonPropertyName("uploaderName")] string UploaderName,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

public sealed record AdminTextureDetailDto(
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
    [property: JsonPropertyName("uploaderId")] Guid? UploaderId,
    [property: JsonPropertyName("uploaderName")] string UploaderName,
    [property: JsonPropertyName("dataUrl")] string DataUrl,
    [property: JsonPropertyName("sizeBytes")] int SizeBytes,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

public sealed record AdminSessionDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("clientToken")] string ClientToken,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("playerId")] Guid? PlayerId,
    [property: JsonPropertyName("playerName")] string? PlayerName,
    [property: JsonPropertyName("authenticationMethod")] AuthenticationMethodType AuthenticationMethod,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("lastUsedAt")] DateTimeOffset LastUsedAt);

public sealed record AdminSessionOverviewDto(
    [property: JsonPropertyName("totalClients")] int TotalClients,
    [property: JsonPropertyName("recentActivities")] IReadOnlyCollection<AdminRecentActivityDto> RecentActivities);

public sealed record AdminRecentActivityDto(
    [property: JsonPropertyName("clientId")] Guid ClientId,
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("playerId")] Guid? PlayerId,
    [property: JsonPropertyName("playerName")] string? PlayerName,
    [property: JsonPropertyName("lastUsedAt")] DateTimeOffset LastUsedAt);

public sealed record AdminOverviewDto(
    [property: JsonPropertyName("totalUsers")] int TotalUsers,
    [property: JsonPropertyName("totalPlayers")] int TotalPlayers,
    [property: JsonPropertyName("totalTextures")] int TotalTextures,
    [property: JsonPropertyName("totalClients")] int TotalClients,
    [property: JsonPropertyName("recentActivities")] IReadOnlyCollection<AdminRecentActivityDto> RecentActivities);

public sealed record AdminUpdateUserRequest(
    [property: JsonPropertyName("nickName")]
    [property: MaxLength(64)]
    string? NickName,

    [property: JsonPropertyName("email")]
    [property: MaxLength(256)]
    string? Email,

    [property: JsonPropertyName("preferredLanguage")]
    [property: MaxLength(16)]
    string? PreferredLanguage,

    [property: JsonPropertyName("role")]
    UserRole? Role,

    [property: JsonPropertyName("maxPlayerCount")]
    [property: Range(0, 100)]
    int? MaxPlayerCount,

    [property: JsonPropertyName("isDisabled")]
    bool? IsDisabled);

public sealed record AdminResetPasswordRequest(
    [param: Required]
    [property: JsonPropertyName("newPassword")]
    string NewPassword);

public sealed record AdminUpdatePlayerRequest(
    [property: JsonPropertyName("name")]
    [property: MaxLength(16)]
    string? Name,

    [property: JsonPropertyName("skinModel")]
    SkinModel? SkinModel,

    [property: JsonPropertyName("skinTextureId")]
    Guid? SkinTextureId,

    [property: JsonPropertyName("clearSkinTexture")]
    bool ClearSkinTexture,

    [property: JsonPropertyName("capeTextureId")]
    Guid? CapeTextureId,

    [property: JsonPropertyName("clearCapeTexture")]
    bool ClearCapeTexture,

    [property: JsonPropertyName("userId")]
    Guid? UserId);

public sealed record AdminUpdateTextureRequest(
    [property: JsonPropertyName("name")]
    [property: MaxLength(80)]
    string? Name,

    [property: JsonPropertyName("descriptionMarkdown")]
    [property: MaxLength(4000)]
    string? DescriptionMarkdown,

    [property: JsonPropertyName("isPublic")]
    bool? IsPublic,

    [property: JsonPropertyName("source")]
    TextureSource? Source,

    [property: JsonPropertyName("skinModel")]
    SkinModel? SkinModel);

public sealed record AdminDeleteUserResponse(
    [property: JsonPropertyName("id")] Guid Id);

public sealed record AdminDeletePlayerResponse(
    [property: JsonPropertyName("id")] Guid Id);

public sealed record AdminDeleteTextureResponse(
    [property: JsonPropertyName("id")] Guid Id);

public sealed record AdminRevokeSessionsResponse(
    [property: JsonPropertyName("userId")] Guid UserId);
