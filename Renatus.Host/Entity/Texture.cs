namespace Renatus.Host.Entity;

public enum TextureType
{
    Skin = 0,
    Cape = 1
}

public enum TextureSource
{
    Original = 0,
    Repost = 1
}

public sealed class Texture
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public TextureType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public TextureSource Source { get; set; } = TextureSource.Original;
    public string DescriptionMarkdown { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public SkinModel SkinModel { get; set; }
    public string DataBase64 { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/png";
    public int Width { get; set; }
    public int Height { get; set; }
    public string? Sha256 { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
