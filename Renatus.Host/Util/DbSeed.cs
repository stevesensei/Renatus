using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Entity;
using Renatus.Host.Service;

namespace Renatus.Host.Util;

public static class DbSeed
{
    public static async Task InitializeAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var isInitialized = await IsInitializedAsync(dbContext, cancellationToken);

        await dbContext.Database.MigrateAsync(cancellationToken);

        if (isInitialized)
        {
            return;
        }

        await SeedAsync(dbContext, cancellationToken);
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

    private static async Task SeedAsync(AppDbContext dbContext, CancellationToken cancellationToken)
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
            PreferredLanguage = "zh_CN"
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
            Type = TextureType.Skin,
            DataBase64 = textureBase64,
            ContentType = "image/png",
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
}
