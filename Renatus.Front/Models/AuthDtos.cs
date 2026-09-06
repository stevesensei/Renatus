using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

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

public sealed class LoginForm
{
    [Required(ErrorMessage = "validation.loginRequired")]
    public string Login { get; set; } = string.Empty;

    [Required(ErrorMessage = "validation.passwordRequired")]
    public string Password { get; set; } = string.Empty;
}

public sealed class RegisterForm
{
    [Required(ErrorMessage = "validation.usernameRequired")]
    [StringLength(64, ErrorMessage = "validation.usernameLength")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "validation.emailRequired")]
    [EmailAddress(ErrorMessage = "validation.invalidEmail")]
    [StringLength(256, ErrorMessage = "validation.emailLength")]
    public string Email { get; set; } = string.Empty;

    [StringLength(64, ErrorMessage = "validation.nicknameLength")]
    public string NickName { get; set; } = string.Empty;

    [Required(ErrorMessage = "validation.passwordRequired")]
    [StringLength(128, MinimumLength = 1, ErrorMessage = "validation.passwordLength")]
    public string Password { get; set; } = string.Empty;
}
