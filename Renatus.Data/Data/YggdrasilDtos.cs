using System.Text.Json.Serialization;

namespace Renatus.Host.Data;

public sealed record AgentDto(string? Name, int? Version);

public sealed record ProfileDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("properties")] IReadOnlyCollection<UserPropertyDto>? Properties = null
    );

public sealed record UserPropertyDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("signature")] string? Signature = null);

public sealed record YggdrasilUserDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("properties")] IReadOnlyCollection<UserPropertyDto> Properties);

public sealed record AuthenticateRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("clientToken")] string? ClientToken,
    [property: JsonPropertyName("agent")] AgentDto? Agent,
    [property: JsonPropertyName("requestUser")] bool RequestUser);

public sealed record AuthenticateResponse(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("clientToken")] string ClientToken,
    [property: JsonPropertyName("selectedProfile")] ProfileDto? SelectedProfile,
    [property: JsonPropertyName("availableProfiles")] IReadOnlyCollection<ProfileDto> AvailableProfiles,
    [property: JsonPropertyName("user")] YggdrasilUserDto? User);

public sealed record RefreshRequest(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("clientToken")] string? ClientToken,
    [property: JsonPropertyName("selectedProfile")] ProfileDto? SelectedProfile,
    [property: JsonPropertyName("requestUser")] bool RequestUser);

public sealed record RefreshResponse(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("clientToken")] string ClientToken,
    [property: JsonPropertyName("selectedProfile")] ProfileDto? SelectedProfile,
    [property: JsonPropertyName("availableProfiles")] IReadOnlyCollection<ProfileDto> AvailableProfiles,
    [property: JsonPropertyName("user")] YggdrasilUserDto? User);

public sealed record ValidateRequest(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("clientToken")] string ClientToken);

public sealed record SignoutRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password);

public sealed record InvalidateRequest(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("clientToken")] string ClientToken);

public sealed record JoinRequest(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("selectedProfile")] string SelectedProfile,
    [property: JsonPropertyName("serverId")] string ServerId);

public sealed record PlayerNameToIdResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name);

public sealed record SessionProfileProperty(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("signature")] string? Signature = null);

public sealed record SessionProfileResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("properties")] IReadOnlyCollection<SessionProfileProperty> Properties);

public sealed record YggdrasilErrorResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("errorMessage")] string ErrorMessage,
    [property: JsonPropertyName("cause")] string? Cause = null);

public sealed record JoinSessionEntry(
    [property: JsonPropertyName("playerId")] Guid PlayerId,
    [property: JsonPropertyName("minecraftUuid")] Guid MinecraftUuid,
    [property: JsonPropertyName("clientId")] Guid ClientId,
    [property: JsonPropertyName("clientVersion")] int ClientVersion,
    [property: JsonPropertyName("issuedAtMilliseconds")] long IssuedAtMilliseconds,
    [property: JsonPropertyName("expiresAtMilliseconds")] long ExpiresAtMilliseconds,
    [property: JsonPropertyName("profileId")] string ProfileId,
    [property: JsonPropertyName("serverId")] string ServerId);
