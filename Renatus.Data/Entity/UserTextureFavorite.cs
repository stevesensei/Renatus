namespace Renatus.Host.Entity;

public sealed class UserTextureFavorite
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid TextureId { get; set; }
    public Texture Texture { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
