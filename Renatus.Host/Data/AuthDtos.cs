using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Renatus.Host.Entity;

namespace Renatus.Host.Data;

public sealed record SessionKeyResponse(
    [property: JsonPropertyName("sessionKeyId")] Guid SessionKeyId,
    [property: JsonPropertyName("publicKey")] string PublicKey,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt);

public sealed record LoginRequest(
    [param: Required]
    [property: JsonPropertyName("sessionKeyId")]
    Guid SessionKeyId,

    [param: Required]
    [property: JsonPropertyName("login")]
    string Login,

    [param: Required]
    [property: JsonPropertyName("encryptedPassword")]
    string EncryptedPassword);

public sealed record RegisterRequest(
    [param: Required]
    [property: JsonPropertyName("sessionKeyId")]
    Guid SessionKeyId,

    [param: Required, MaxLength(64)]
    [property: JsonPropertyName("username")]
    string Username,

    [param: Required, EmailAddress, MaxLength(256)]
    [property: JsonPropertyName("email")]
    string Email,

    [param: MaxLength(64)]
    [property: JsonPropertyName("nickName")]
    string? NickName,

    [param: Required]
    [property: JsonPropertyName("encryptedPassword")]
    string EncryptedPassword);

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

public sealed record AuthErrorResponse(
    [property: JsonPropertyName("message")] string Message);
