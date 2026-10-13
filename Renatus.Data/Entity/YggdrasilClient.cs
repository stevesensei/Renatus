namespace Renatus.Host.Entity;

public sealed class YggdrasilClient
{
    public Guid Id { get; set; }
    public string ClientToken { get; set; } = string.Empty;
    public int Version { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? PlayerId { get; set; }
    public Player? Player { get; set; }
    public AuthenticationMethodType AuthenticationMethod { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
}
