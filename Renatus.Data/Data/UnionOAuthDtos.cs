using System.Text.Json.Serialization;

namespace Renatus.Host.Data;

public sealed record UnionOAuthPublicKeyResponse(string SignaturePublicKey);

public sealed record UnionOAuthInteractionResponse(
    Guid InteractionId,
    DateTimeOffset ExpiresAt,
    IReadOnlyDictionary<string, string> Continuation);

public sealed record UnionOAuthCompleteRequest(bool Approve);

public sealed record UnionOAuthCompleteResponse(string RedirectUri);

public sealed record UnionOAuthInteraction(
    DateTimeOffset ExpiresAt,
    Dictionary<string, string> Continuation);

public sealed record UnionOAuthUserInfo(
    [property: JsonPropertyName("uid")] long Uid,
    [property: JsonPropertyName("nickname")] string Nickname,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("expires_at")] long ExpiresAt);

public sealed record UnionOAuthToken(
    [property: JsonPropertyName("userInfo")] string UserInfo,
    [property: JsonPropertyName("mac")] string Mac,
    [property: JsonPropertyName("signature")] string Signature);

public sealed record UnionOAuthBackendKeyResponse(
    [property: JsonPropertyName("publicKey")] string PublicKey);
