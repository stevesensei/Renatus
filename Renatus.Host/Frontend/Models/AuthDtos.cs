using System.ComponentModel.DataAnnotations;

namespace Renatus.Host.Frontend.Models;

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
