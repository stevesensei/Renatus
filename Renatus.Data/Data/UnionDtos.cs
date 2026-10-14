using System.Text.Json;
using System.Text.Json.Serialization;

namespace Renatus.Host.Data;

public sealed record UnionMemberInfoResponse(
    [property: JsonPropertyName("yggdrasilApiVersion")] string YggdrasilApiVersion,
    [property: JsonPropertyName("serverListVersion")] long ServerListVersion,
    [property: JsonPropertyName("privateKeyVersion")] long PrivateKeyVersion,
    [property: JsonPropertyName("enabledFeatures")] string[] EnabledFeatures);

public sealed record UnionBackendKeyRequest(
    [property: JsonPropertyName("key")] string Key);

public sealed record UnionRemapRequest(
    [property: JsonPropertyName("remapped_uuid")] Dictionary<string, string> RemappedUuid);

public sealed record UnionDiagnoseRequest(
    [property: JsonPropertyName("nonce")] JsonElement? Nonce);

public sealed record UnionDiagnoseResponse(
    [property: JsonPropertyName("nonce"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] JsonElement? Nonce,
    [property: JsonPropertyName("timestamp")] double Timestamp);

public sealed record UnionEmailResponse(
    [property: JsonPropertyName("email")] string Email);

public sealed record UnionAdminStatusResponse(
    bool Enabled,
    long ServerListVersion,
    long PrivateKeyVersion,
    bool NeedsProfileSync,
    DateTimeOffset? LastProfileSyncAt,
    string? LastProfileSyncError);

public sealed record UnionAdminSettingsResponse(
    bool Enabled,
    string ApiRoot,
    string MemberCode,
    bool HasMemberKey,
    int RequestTimeoutSeconds);

public sealed record UnionAdminUpdateSettingsRequest(
    bool Enabled,
    string ApiRoot,
    string MemberCode,
    string? MemberKey,
    int RequestTimeoutSeconds);

public sealed record UnionAdminOperationResponse(
    string Status,
    int? StatusCode = null,
    JsonElement? Data = null);

public sealed record UnionServerListResponse(
    [property: JsonPropertyName("servers")] List<Data.Config.UnionBackend> Servers,
    [property: JsonPropertyName("version")] long Version);

public sealed record UnionPrivateKeyResponse(
    [property: JsonPropertyName("privateKey")] string PrivateKey,
    [property: JsonPropertyName("privateKeyVersion")] long PrivateKeyVersion);

public sealed record UnionHostMetadataResponse(
    [property: JsonPropertyName("union_host_signature_public_key")] string SignaturePublicKey);

public sealed record UnionProfileSyncRequest(
    [property: JsonPropertyName("profileList")] Dictionary<string, string> ProfileList);

