namespace Renatus.Host.Entity;

public enum SkinModel
{
    Default = 0,
    Slim = 1
}

public sealed class Player
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public SkinModel SkinModel { get; set; }
    public Guid? SkinTextureId { get; set; }
    public Texture? SkinTexture { get; set; }
    public Guid? CapeTextureId { get; set; }
    public Texture? CapeTexture { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<YggdrasilClient> Clients { get; set; } = new List<YggdrasilClient>();
}
