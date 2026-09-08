namespace Renatus.Host.Entity;

public sealed class User
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string NormalizedUserName { get; set; } = string.Empty;
    public string NickName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string? AvatarBase64 { get; set; }
    public string PreferredLanguage { get; set; } = "zh_CN";
    public UserRole Role { get; set; } = UserRole.Player;
    public int MaxPlayerCount { get; set; } = 3;
    public bool IsDisabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<UserAuthenticationMethod> AuthenticationMethods { get; set; } = new List<UserAuthenticationMethod>();
    public ICollection<Player> Players { get; set; } = new List<Player>();
    public ICollection<Texture> Textures { get; set; } = new List<Texture>();
    public ICollection<YggdrasilClient> Clients { get; set; } = new List<YggdrasilClient>();
}
