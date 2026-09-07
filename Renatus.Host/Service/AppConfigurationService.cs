using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;
using Renatus.Host.Service.Cache;

namespace Renatus.Host.Service;

public sealed class AppConfigurationService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    ICacheService cacheService)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<SiteOptions> GetSiteOptionsAsync(CancellationToken cancellationToken)
    {
        return GetAsync<SiteOptions>(AppConfigurationKeys.Site, cancellationToken);
    }

    public Task<SiteAnnouncementOptions> GetLoginAnnouncementAsync(CancellationToken cancellationToken)
    {
        return GetAsync<SiteAnnouncementOptions>(AppConfigurationKeys.LoginAnnouncement, cancellationToken);
    }

    public Task<SiteAnnouncementOptions> GetSiteAnnouncementAsync(CancellationToken cancellationToken)
    {
        return GetAsync<SiteAnnouncementOptions>(AppConfigurationKeys.SiteAnnouncement, cancellationToken);
    }

    public async Task<YggdrasilOptions> GetYggdrasilOptionsAsync(CancellationToken cancellationToken)
    {
        var siteTask = GetSiteOptionsAsync(cancellationToken);
        var yggdrasilTask = GetAsync<YggdrasilOptions>(AppConfigurationKeys.Yggdrasil, cancellationToken);
        await Task.WhenAll(siteTask, yggdrasilTask);

        var site = await siteTask;
        var yggdrasil = await yggdrasilTask;
        var publicBaseUrl = NormalizePublicBaseUrl(site.PublicBaseUrl);
        yggdrasil.PublicBaseUrl = publicBaseUrl;
        yggdrasil.HomepageUrl = publicBaseUrl;
        yggdrasil.RegisterUrl = $"{publicBaseUrl}/login";
        yggdrasil.SkinDomains = [RemoveProtocolAndSlashes(publicBaseUrl)];
        return yggdrasil;
    }

    private async Task<T> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        var cacheKey = $"renatus:configuration:{key}";
        var cached = await cacheService.GetAsync<T>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var configuration = await dbContext.AppConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Key == key, cancellationToken);
        if (configuration is null)
        {
            throw new InvalidOperationException($"Configuration '{key}' has not been initialized.");
        }

        var value = JsonSerializer.Deserialize<T>(configuration.Value, JsonOptions);
        if (value is null)
        {
            throw new InvalidOperationException($"Configuration '{key}' contains invalid JSON.");
        }

        await cacheService.SetAsync(cacheKey, value, CacheTtl, cancellationToken);
        return value;
    }

    public static string NormalizePublicBaseUrl(string? publicBaseUrl)
    {
        return (publicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
    }

    private static string RemoveProtocolAndSlashes(string publicBaseUrl)
    {
        var val = publicBaseUrl
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim('/');
        if (val.Contains(':'))
        {
            val = val.Substring(0, val.IndexOf(':'));
        }
        return val;
    }
}
