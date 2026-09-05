namespace Renatus.Host.Entity;

public enum TextureType
{
    Skin = 0,
    Cape = 1
}

public sealed class Texture
{
    public Guid Id { get; set; }
    public TextureType Type { get; set; }
    public string DataBase64 { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/png";
    public string? Sha256 { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
