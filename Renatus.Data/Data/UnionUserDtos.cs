using System.Text.Json;

namespace Renatus.Host.Data;

public sealed record UnionProfilesResponse(IReadOnlyList<UnionProfileDto> Profiles);

public sealed record UnionProfileDto(
    Guid PlayerId,
    Guid MinecraftUuid,
    string Name,
    JsonElement Detail,
    IReadOnlyList<JsonElement> Conflicts);

public sealed record UnionDirectoryResponse(IReadOnlyList<Config.UnionBackend> Servers);

public sealed record UnionSecurityLevelResponse(int Level);

public sealed record UnionBindTokenResponse(string Token);

public sealed record UnionBindRequest(string Token);

public sealed record UnionRemapProfileRequest(Guid TargetMinecraftUuid);

public sealed record UnionBlacklistCreateRequest(string Email, string Reason);

public sealed record UnionActivityPageResponse(
    IReadOnlyList<UnionActivityDto> Items,
    int Page,
    int PageSize,
    long Total);

public sealed record UnionActivityDto(
    Guid Id,
    string Action,
    Guid? UserId,
    string? UserName,
    Guid? PlayerId,
    string? PlayerName,
    JsonElement Parameters,
    string? IpAddress,
    DateTimeOffset CreatedAt);
