using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;
using Renatus.Host.Entity;
using Renatus.Host.Service;

namespace Renatus.Host.Utils;

public static class DbSeed
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task InitializeAsync(
        AppDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var isInitialized = await IsInitializedAsync(dbContext, cancellationToken);

        await dbContext.Database.MigrateAsync(cancellationToken);
        await SeedConfigurationsAsync(dbContext, configuration, cancellationToken);

        if (isInitialized)
        {
            return;
        }

        await SeedTestDataAsync(dbContext, cancellationToken);
    }

    private static async Task<bool> IsInitializedAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        if (!await dbContext.Database.CanConnectAsync(cancellationToken))
        {
            return false;
        }

        var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken);
        return appliedMigrations.Any();
    }

    private static async Task SeedConfigurationsAsync(
        AppDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var siteOptions = configuration.GetSection(SiteOptions.SectionName).Get<SiteOptions>() ?? new SiteOptions();
        siteOptions.PublicBaseUrl = AppConfigurationService.NormalizePublicBaseUrl(siteOptions.PublicBaseUrl);

        var loginAnnouncement = configuration
            .GetSection("LoginAnnouncement")
            .Get<SiteAnnouncementOptions>() ?? new SiteAnnouncementOptions();
        var siteAnnouncement = configuration
            .GetSection("SiteAnnouncement")
            .Get<SiteAnnouncementOptions>() ?? new SiteAnnouncementOptions();
        var yggdrasilOptions = configuration
            .GetSection("Yggdrasil")
            .Get<YggdrasilOptions>() ?? new YggdrasilOptions();
        yggdrasilOptions.PublicBaseUrl = string.Empty;
        yggdrasilOptions.HomepageUrl = string.Empty;
        yggdrasilOptions.RegisterUrl = string.Empty;
        yggdrasilOptions.SkinDomains = [];

        var configurations = new[]
        {
            new ConfigurationSeed(AppConfigurationKeys.Site, siteOptions),
            new ConfigurationSeed(AppConfigurationKeys.LoginAnnouncement, loginAnnouncement),
            new ConfigurationSeed(AppConfigurationKeys.SiteAnnouncement, siteAnnouncement),
            new ConfigurationSeed(AppConfigurationKeys.Yggdrasil, yggdrasilOptions),
            new ConfigurationSeed(AppConfigurationKeys.Union,
                configuration.GetSection(UnionOptions.SectionName).Get<UnionOptions>() ?? new UnionOptions()),
            new ConfigurationSeed(AppConfigurationKeys.UnionState, new UnionState()),
            new ConfigurationSeed(AppConfigurationKeys.YggdrasilConnect,
                configuration.GetSection(YggdrasilConnectOptions.SectionName).Get<YggdrasilConnectOptions>()
                ?? new YggdrasilConnectOptions())
        };

        foreach (var seed in configurations)
        {
            if (await dbContext.AppConfigurations.AnyAsync(entity => entity.Key == seed.Key, cancellationToken))
            {
                continue;
            }

            dbContext.AppConfigurations.Add(new AppConfiguration
            {
                Key = seed.Key,
                Value = JsonSerializer.Serialize(seed.Value, JsonOptions)
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await SeedPrivateKeysAsync(dbContext, cancellationToken);
    }

    private static async Task SeedPrivateKeysAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        var keys = new[]
        {
            (AppConfigurationKeys.Yggdrasil, "signingPrivateKeyPem"),
            (AppConfigurationKeys.Yggdrasil, "textureSigningPrivateKeyPem"),
            (AppConfigurationKeys.Union, "oAuth2SigningPrivateKeyPem"),
            (AppConfigurationKeys.YggdrasilConnect, "signingPrivateKeyPem"),
            (AppConfigurationKeys.YggdrasilConnect, "encryptionPrivateKeyPem")
        };
        foreach (var (key, property) in keys)
        {
            var row = await dbContext.AppConfigurations.SingleAsync(entity => entity.Key == key, cancellationToken);
            var value = JsonNode.Parse(row.Value, new JsonNodeOptions { PropertyNameCaseInsensitive = true })
                as JsonObject ?? throw new InvalidOperationException($"Configuration '{key}' must be an object.");
            if (!string.IsNullOrWhiteSpace(value[property]?.GetValue<string>()))
            {
                continue;
            }

            using var rsa = RSA.Create(2048);
            value[property] = rsa.ExportPkcs8PrivateKeyPem();
            row.Value = value.ToJsonString(JsonOptions);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedTestDataAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        if (await dbContext.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var userId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var textureId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var textureBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgwJ/l0xSPAAAAABJRU5ErkJggg==";

        var user = new User
        {
            Id = userId,
            UserName = "test",
            NormalizedUserName = YggdrasilFormat.Normalize("test"),
            NickName = "Test User",
            Email = "test@example.com",
            NormalizedEmail = YggdrasilFormat.Normalize("test@example.com"),
            PreferredLanguage = "zh_CN",
            Role = UserRole.Player,
            MaxPlayerCount = 3
        };

        var authenticationMethod = new UserAuthenticationMethod
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = AuthenticationMethodType.Password,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password")
        };

        var texture = new Texture
        {
            Id = textureId,
            UserId = userId,
            Type = TextureType.Skin,
            Name = "Blue Ice",
            Source = TextureSource.Original,
            DescriptionMarkdown = "Renatus 默认演示材质。",
            IsPublic = true,
            SkinModel = SkinModel.Default,
            DataBase64 = textureBase64,
            ContentType = "image/png",
            Width = 1,
            Height = 1,
            Sha256 = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(textureBase64))).ToLowerInvariant()
        };

        var player = new Player
        {
            Id = playerId,
            UserId = userId,
            Name = "TestPlayer",
            NormalizedName = YggdrasilFormat.Normalize("TestPlayer"),
            SkinModel = SkinModel.Default,
            SkinTextureId = textureId
        };

        var client = new YggdrasilClient
        {
            Id = clientId,
            ClientToken = "seed-client-token",
            UserId = userId,
            PlayerId = playerId,
            AuthenticationMethod = AuthenticationMethodType.Password,
            LastUsedAt = DateTimeOffset.UtcNow
        };

        dbContext.Users.Add(user);
        dbContext.UserAuthenticationMethods.Add(authenticationMethod);
        dbContext.Textures.Add(texture);
        dbContext.Players.Add(player);
        dbContext.YggdrasilClients.Add(client);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed record ConfigurationSeed(string Key, object Value);
}
