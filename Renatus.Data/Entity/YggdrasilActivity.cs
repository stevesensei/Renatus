namespace Renatus.Host.Entity;

public sealed class YggdrasilActivity
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public Guid? PlayerId { get; set; }
    public Player? Player { get; set; }
    public string Parameters { get; set; } = "{}";
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
