using System.Text.Json.Serialization;

namespace Renatus.Front.Models;

public enum UserRole
{
    Player = 0,
    Admin = 1,
}

public sealed record SessionKeyResponse(
    [property: JsonPropertyName("sessionKeyId")] Guid SessionKeyId,
    [property: JsonPropertyName("publicKey")] string PublicKey,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt);

public sealed record LoginRequest(
    [property: JsonPropertyName("sessionKeyId")] Guid SessionKeyId,
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("encryptedPassword")] string EncryptedPassword);

public sealed record RegisterRequest(
    [property: JsonPropertyName("sessionKeyId")] Guid SessionKeyId,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("nickName")] string? NickName,
    [property: JsonPropertyName("encryptedPassword")] string EncryptedPassword);

public sealed record AuthResponse(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("tokenType")] string TokenType,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("user")] AuthUserDto User);

public sealed record AuthUserDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("nickName")] string NickName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] UserRole Role);
