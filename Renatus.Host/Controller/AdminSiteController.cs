using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;
using Renatus.Host.Service;

namespace Renatus.Host.Controller;

[ApiController]
[Route("api/admin")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public sealed class AdminSiteController(AppConfigurationService configurationService) : ControllerBase
{
    [HttpGet("site")]
    public async Task<GlobalResponse<SiteOptions>> GetSite(CancellationToken cancellationToken)
        => GlobalResponse<SiteOptions>.Success(await configurationService.GetSiteOptionsAsync(cancellationToken));

    [HttpPut("site")]
    public async Task<GlobalResponse<SiteOptions>> UpdateSite(
        UpdateSiteRequest request, CancellationToken cancellationToken)
    {
        if (!IsHttpUrl(request.PublicBaseUrl) || !IsResourceUrl(request.HeroImage)
            || request.Buttons.Any(button => button is null || !IsResourceUrl(button.Url)
                || button.Target is not (null or "" or "_self" or "_blank")
                || button.Style is not (null or "" or "filled" or "outlined" or "text" or "secondary" or "tertiary")))
        {
            return GlobalResponse<SiteOptions>.ResultError(message: "Site URLs, button styles or targets are invalid.");
        }

        var options = new SiteOptions
        {
            Title = request.Title,
            Description = request.Description ?? string.Empty,
            HeroImage = request.HeroImage ?? string.Empty,
            PublicBaseUrl = request.PublicBaseUrl,
            Copyright = request.Copyright ?? string.Empty,
            IcpRegistrationNumber = request.IcpRegistrationNumber ?? string.Empty,
            Buttons = request.Buttons.Select(button => new SiteButtonOptions
            {
                Label = button.Label ?? string.Empty,
                Url = button.Url ?? string.Empty,
                Style = button.Style,
                Target = button.Target
            }).ToList()
        };
        await configurationService.UpdateSiteOptionsAsync(options, cancellationToken);
        return GlobalResponse<SiteOptions>.Success(options);
    }

    [HttpGet("announcements/{kind}")]
    public async Task<GlobalResponse<SiteAnnouncementOptions>> GetAnnouncement(
        string kind, CancellationToken cancellationToken)
    {
        var result = kind switch
        {
            "login" => await configurationService.GetLoginAnnouncementAsync(cancellationToken),
            "site" => await configurationService.GetSiteAnnouncementAsync(cancellationToken),
            _ => null
        };
        return result is null
            ? GlobalResponse<SiteAnnouncementOptions>.ResultError(message: "Announcement kind must be login or site.")
            : GlobalResponse<SiteAnnouncementOptions>.Success(result);
    }

    [HttpPut("announcements/{kind}")]
    public async Task<GlobalResponse<SiteAnnouncementOptions>> UpdateAnnouncement(
        string kind, UpdateAnnouncementRequest request, CancellationToken cancellationToken)
    {
        if (kind is not ("login" or "site") || !IsResourceUrl(request.Image))
            return GlobalResponse<SiteAnnouncementOptions>.ResultError(message: "Announcement kind or image URL is invalid.");

        var options = new SiteAnnouncementOptions
        {
            Title = request.Title,
            Body = request.Body ?? string.Empty,
            Image = request.Image ?? string.Empty
        };
        if (kind == "login") await configurationService.UpdateLoginAnnouncementAsync(options, cancellationToken);
        else await configurationService.UpdateSiteAnnouncementAsync(options, cancellationToken);
        return GlobalResponse<SiteAnnouncementOptions>.Success(options);
    }

    private static bool IsHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsResourceUrl(string? value)
        => string.IsNullOrWhiteSpace(value)
            || (value.StartsWith('/') && !value.StartsWith("//") && value.IndexOf((char)92) < 0)
            || IsHttpUrl(value);
}

