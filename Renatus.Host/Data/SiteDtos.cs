using System.Text.Json.Serialization;

namespace Renatus.Host.Data;

public sealed record SiteConfigDto(
    [property: JsonPropertyName("heroImage")] string HeroImage,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("loginAnnouncement")] SiteAnnouncementDto LoginAnnouncement,
    [property: JsonPropertyName("buttons")] IReadOnlyList<SiteButtonDto> Buttons);

public sealed record SiteAnnouncementDto(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("image")] string Image);

public sealed record SiteButtonDto(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("style")] string? Style,
    [property: JsonPropertyName("target")] string? Target);
