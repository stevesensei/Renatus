namespace Renatus.Front.Models;

public sealed class YggdrasilOptions
{
    public const string SectionName = "Yggdrasil";

    public Uri PublicBaseUrl { get; set; } = new("http://localhost:5294");
}
