namespace Renatus.Host.Entity;

public enum AuthenticationMethodType
{
    Password = 0,
    Totp = 1,
    OAuth = 2
}

public sealed class UserAuthenticationMethod
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public AuthenticationMethodType Type { get; set; }
    public string? Provider { get; set; }
    public string? Subject { get; set; }
    public string? PasswordHash { get; set; }
    public string? TotpSecret { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
