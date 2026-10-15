using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Renatus.Host.Data;

public sealed record ConnectProfileDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name);

public sealed record ConnectInteractionDto(
    [property: JsonPropertyName("interactionId")] string InteractionId,
    [property: JsonPropertyName("clientId")] string ClientId,
    [property: JsonPropertyName("clientName")] string ClientName,
    [property: JsonPropertyName("scopes")] IReadOnlyList<string> Scopes,
    [property: JsonPropertyName("profiles")] IReadOnlyList<ConnectProfileDto> Profiles,
    [property: JsonPropertyName("deviceFlow")] bool DeviceFlow,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt);

public sealed record ConnectDecisionRequest(
    [property: JsonPropertyName("approve")] bool Approve,
    [property: JsonPropertyName("playerId")] string? PlayerId);

public sealed record ConnectCompletionDto(
    [property: JsonPropertyName("completionUrl")] string CompletionUrl);

public sealed record ConnectDeviceResolveRequest(
    [param: Required, MaxLength(32)]
    [property: JsonPropertyName("userCode")] string UserCode);

public sealed record ConnectGrantDto(
    [property: JsonPropertyName("authorizationId")] string AuthorizationId,
    [property: JsonPropertyName("clientId")] string ClientId,
    [property: JsonPropertyName("clientName")] string ClientName,
    [property: JsonPropertyName("scopes")] IReadOnlyList<string> Scopes,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

public sealed record ConnectActivityDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("playerId")] Guid? PlayerId,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

public sealed record ConnectConfigurationDto(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("sharedClientId")] string SharedClientId,
    [property: JsonPropertyName("sharedClientRedirectUris")] IReadOnlyList<string> SharedClientRedirectUris,
    [property: JsonPropertyName("allowImplicitFlow")] bool AllowImplicitFlow,
    [property: JsonPropertyName("hasSigningKey")] bool HasSigningKey,
    [property: JsonPropertyName("hasEncryptionKey")] bool HasEncryptionKey,
    [property: JsonPropertyName("requiresRestart")] bool RequiresRestart,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("accessTokenTtlSeconds")] int AccessTokenTtlSeconds,
    [property: JsonPropertyName("refreshTokenTtlSeconds")] int RefreshTokenTtlSeconds,
    [property: JsonPropertyName("authorizationCodeTtlSeconds")] int AuthorizationCodeTtlSeconds,
    [property: JsonPropertyName("deviceCodeTtlSeconds")] int DeviceCodeTtlSeconds);

public sealed record UpdateConnectConfigurationRequest(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [param: MaxLength(512)] [property: JsonPropertyName("issuer")] string Issuer,
    [param: MaxLength(128)] [property: JsonPropertyName("sharedClientId")] string SharedClientId,
    [property: JsonPropertyName("sharedClientRedirectUris")] IReadOnlyList<string> SharedClientRedirectUris,
    [property: JsonPropertyName("allowImplicitFlow")] bool AllowImplicitFlow,
    [property: JsonPropertyName("replaceSigningKey")] bool ReplaceSigningKey,
    [property: JsonPropertyName("replaceEncryptionKey")] bool ReplaceEncryptionKey,
    [property: JsonPropertyName("accessTokenTtlSeconds")] int AccessTokenTtlSeconds,
    [property: JsonPropertyName("refreshTokenTtlSeconds")] int RefreshTokenTtlSeconds,
    [property: JsonPropertyName("authorizationCodeTtlSeconds")] int AuthorizationCodeTtlSeconds,
    [property: JsonPropertyName("deviceCodeTtlSeconds")] int DeviceCodeTtlSeconds);

public sealed record ConnectClientDto(
    [property: JsonPropertyName("clientId")] string ClientId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("redirectUris")] IReadOnlyList<string> RedirectUris,
    [property: JsonPropertyName("confidential")] bool Confidential,
    [property: JsonPropertyName("allowImplicitFlow")] bool AllowImplicitFlow);

public sealed record ConnectClientSecretDto(
    [property: JsonPropertyName("clientId")] string ClientId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("redirectUris")] IReadOnlyList<string> RedirectUris,
    [property: JsonPropertyName("confidential")] bool Confidential,
    [property: JsonPropertyName("allowImplicitFlow")] bool AllowImplicitFlow,
    [property: JsonPropertyName("clientSecret")] string? ClientSecret);

public sealed record CreateConnectClientRequest(
    [param: Required, MaxLength(128)] [property: JsonPropertyName("clientId")] string ClientId,
    [param: Required, MaxLength(200)] [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("redirectUris")] IReadOnlyList<string> RedirectUris,
    [property: JsonPropertyName("confidential")] bool Confidential,
    [property: JsonPropertyName("allowImplicitFlow")] bool AllowImplicitFlow);

public sealed record UpdateConnectClientRequest(
    [param: Required, MaxLength(200)] [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("redirectUris")] IReadOnlyList<string> RedirectUris,
    [property: JsonPropertyName("confidential")] bool Confidential,
    [property: JsonPropertyName("allowImplicitFlow")] bool AllowImplicitFlow);
