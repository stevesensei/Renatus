using Renatus.Host.Data;
using Renatus.Host.Data.Config;

namespace Renatus.Host.Service;

public sealed class SiteService(AppConfigurationService configurationService)
{
    public async Task<SiteConfigDto> GetConfigAsync(CancellationToken cancellationToken)
    {
        var siteTask = configurationService.GetSiteOptionsAsync(cancellationToken);
        var loginAnnouncementTask = configurationService.GetLoginAnnouncementAsync(cancellationToken);
        var siteAnnouncementTask = configurationService.GetSiteAnnouncementAsync(cancellationToken);
        await Task.WhenAll(siteTask, loginAnnouncementTask, siteAnnouncementTask);

        var site = await siteTask;
        var loginAnnouncement = await loginAnnouncementTask;
        var siteAnnouncement = await siteAnnouncementTask;
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
                loginAnnouncement.Title.Trim(),
                loginAnnouncement.Body.Trim(),
                loginAnnouncement.Image.Trim()),
            new SiteAnnouncementDto(
                siteAnnouncement.Title.Trim(),
                siteAnnouncement.Body.Trim(),
                siteAnnouncement.Image.Trim()),
            buttons,
            AppConfigurationService.NormalizePublicBaseUrl(site.PublicBaseUrl),
            site.Copyright.Trim(),
            site.IcpRegistrationNumber.Trim());
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
