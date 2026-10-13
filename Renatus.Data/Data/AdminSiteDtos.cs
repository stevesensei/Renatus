using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Renatus.Host.Data;

public sealed record UpdateSiteRequest(
    [param: Required, MaxLength(120)]
    [property: JsonPropertyName("title")] string Title,
    [param: MaxLength(500)]
    [property: JsonPropertyName("description")] string Description,
    [param: MaxLength(1024)]
    [property: JsonPropertyName("heroImage")] string HeroImage,
    [param: Required, MaxLength(512)]
    [property: JsonPropertyName("publicBaseUrl")] string PublicBaseUrl,
    [param: Required, MaxLength(3)]
    [property: JsonPropertyName("buttons")] IReadOnlyList<SiteButtonRequest> Buttons,
    [param: MaxLength(500)]
    [property: JsonPropertyName("copyright")] string? Copyright = null,
    [param: MaxLength(80)]
    [property: JsonPropertyName("icpRegistrationNumber")] string? IcpRegistrationNumber = null);

public sealed record SiteButtonRequest(
    [param: MaxLength(80)]
    [property: JsonPropertyName("label")] string Label,
    [param: MaxLength(1024)]
    [property: JsonPropertyName("url")] string Url,
    [param: MaxLength(32)]
    [property: JsonPropertyName("style")] string? Style,
    [param: MaxLength(32)]
    [property: JsonPropertyName("target")] string? Target);

public sealed record UpdateAnnouncementRequest(
    [param: Required, MaxLength(160)]
    [property: JsonPropertyName("title")] string Title,
    [param: MaxLength(16000)]
    [property: JsonPropertyName("body")] string Body,
    [param: MaxLength(1024)]
    [property: JsonPropertyName("image")] string Image);

