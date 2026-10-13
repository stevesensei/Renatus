using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Renatus.Host.Entity;

namespace Renatus.Host.Data;

public sealed record AccountProfileDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("nickName")] string NickName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("avatarBase64")] string? AvatarBase64,
    [property: JsonPropertyName("role")] UserRole Role);

public sealed record UpdateAccountProfileRequest(
    [param: Required, MaxLength(64)]
    [property: JsonPropertyName("nickName")]
    string NickName,

    [param: Required, EmailAddress, MaxLength(256)]
    [property: JsonPropertyName("email")]
    string Email,

    [param: MaxLength(2_000_000)]
    [property: JsonPropertyName("avatarBase64")]
    string? AvatarBase64);

public sealed record ChangePasswordRequest(
    [param: Required]
    [property: JsonPropertyName("currentPassword")]
    string CurrentPassword,

    [param: Required]
    [property: JsonPropertyName("newPassword")]
    string NewPassword);
