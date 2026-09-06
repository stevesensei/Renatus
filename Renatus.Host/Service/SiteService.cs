using Microsoft.Extensions.Options;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;

namespace Renatus.Host.Service;

public sealed class SiteService(IOptions<SiteOptions> options)
{
    public SiteConfigDto GetConfig()
    {
        var site = options.Value;
        var announcement = site.LoginAnnouncement ?? new SiteAnnouncementOptions();
        var buttons = (site.Buttons ?? [])
            .Where(button => !string.IsNullOrWhiteSpace(button.Label) && !string.IsNullOrWhiteSpace(button.Url))
            .Take(3)
            .Select(button => new SiteButtonDto(
                button.Label.Trim(),
                button.Url.Trim(),
                NormalizeOptional(button.Style),
                NormalizeOptional(button.Target)))
            .ToArray();

        return new SiteConfigDto(
            site.HeroImage.Trim(),
            site.Title.Trim(),
            site.Description.Trim(),
            new SiteAnnouncementDto(
                announcement.Title.Trim(),
                announcement.Body.Trim(),
                announcement.Image.Trim()),
            buttons);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
