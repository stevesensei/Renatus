using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;
using Renatus.Host.Entity;
using Renatus.Host.Service.Cache;
using Renatus.Host.Utils;

namespace Renatus.Host.Service;

public sealed partial class YggdrasilService(
    AppDbContext dbContext,
    YggdrasilSigningKey signingKey,
    ICacheService cacheService,
    AppConfigurationService configurationService)
{
    #region Auth

    public async Task<AuthenticateResponse> AuthenticateAsync(AuthenticateRequest request, CancellationToken cancellationToken)
    {
        var options = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
        var (user, selectedPlayer) = await AuthenticateUserAsync(request.Username, request.Password, cancellationToken);
        var clientToken = string.IsNullOrWhiteSpace(request.ClientToken)
            ? Guid.NewGuid().ToString("N")
            : request.ClientToken;

        var client = await dbContext.YggdrasilClients
            .FirstOrDefaultAsync(entity => entity.UserId == user.Id && entity.ClientToken == clientToken, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (client is null)
        {
            client = new YggdrasilClient
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ClientToken = clientToken,
                Version = 0,
                PlayerId = selectedPlayer?.Id,
                AuthenticationMethod = AuthenticationMethodType.Password,
                CreatedAt = now,
                LastUsedAt = now
            };
            dbContext.YggdrasilClients.Add(client);
        }
        else
        {
            client.Version += 1;
            client.PlayerId = selectedPlayer?.Id;
            client.AuthenticationMethod = AuthenticationMethodType.Password;
            client.LastUsedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = CreateAccessToken(client, options);
        var selectedProfile = selectedPlayer is null ? null : ToProfile(selectedPlayer);
        return new AuthenticateResponse(
            accessToken,
            client.ClientToken,
            selectedProfile,
            user.Players.Select(ToProfile).ToArray(),
            request.RequestUser ? ToUser(user) : null);
    }

    public async Task<RefreshResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var options = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
        var client = await ValidateAccessTokenAsync(request.AccessToken, request.ClientToken, false, cancellationToken);
        Player? selectedPlayer = client.Player;

        if (request.SelectedProfile is not null)
        {
            if (client.PlayerId is not null)
            {
                throw new YggdrasilException(
                    StatusCodes.Status400BadRequest,
                    "IllegalArgumentException",
                    "Access token already has a profile assigned.");
            }

            var selectedProfileId = YggdrasilFormat.ParseUnsignedUuid(request.SelectedProfile.Id);
            selectedPlayer = client.User.Players.FirstOrDefault(player => player.Id == selectedProfileId);
            if (selectedPlayer is null)
            {
                throw YggdrasilException.InvalidToken();
            }

            client.PlayerId = selectedPlayer.Id;
        }

        client.Version += 1;
        client.LastUsedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = CreateAccessToken(client, options);
        return new RefreshResponse(
            accessToken,
            client.ClientToken,
            selectedPlayer is null ? null : ToProfile(selectedPlayer),
            client.User.Players.Select(ToProfile).ToArray(),
            request.RequestUser ? ToUser(client.User) : null);
    }

    public async Task ValidateAsync(ValidateRequest request, CancellationToken cancellationToken)
    {
        await ValidateAccessTokenAsync(request.AccessToken, request.ClientToken, false, cancellationToken);
    }

    public async Task InvalidateAsync(InvalidateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var client = await ValidateAccessTokenAsync(request.AccessToken, request.ClientToken, false, cancellationToken);
            client.Version += 1;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (YggdrasilException)
        {
        }
    }

    public async Task SignoutAsync(SignoutRequest request, CancellationToken cancellationToken)
    {
        var (user, _) = await AuthenticateUserAsync(request.Username, request.Password, cancellationToken);
        await dbContext.YggdrasilClients
            .Where(client => client.UserId == user.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(client => client.Version, client => client.Version + 1), cancellationToken);
    }

    private async Task<(User User, Player? SelectedPlayer)> AuthenticateUserAsync(
        string login,
        string password,
        CancellationToken cancellationToken)
    {
        var normalizedLogin = YggdrasilFormat.Normalize(login);
        Player? selectedPlayer = await dbContext.Players
            .AsSplitQuery()
            .Include(player => player.User)
            .ThenInclude(user => user.AuthenticationMethods)
            .Include(player => player.User)
            .ThenInclude(user => user.Players)
            .FirstOrDefaultAsync(player => player.NormalizedName == normalizedLogin, cancellationToken);

        var user = selectedPlayer?.User;
        if (user is null)
        {
            user = await dbContext.Users
                .AsSplitQuery()
                .Include(entity => entity.AuthenticationMethods)
                .Include(entity => entity.Players)
                .FirstOrDefaultAsync(
                    entity => entity.NormalizedUserName == normalizedLogin || entity.NormalizedEmail == normalizedLogin,
                    cancellationToken);
        }

        if (user is null || user.IsDisabled)
        {
            throw YggdrasilException.InvalidCredentials();
        }

        var passwordMethod = user.AuthenticationMethods.FirstOrDefault(method => method.Type == AuthenticationMethodType.Password);
        if (passwordMethod?.PasswordHash is null || !VerifyPassword(password, passwordMethod.PasswordHash))
        {
            throw YggdrasilException.InvalidCredentials();
        }

        if (selectedPlayer is null && user.Players.Count == 1)
        {
            selectedPlayer = user.Players.Single();
        }

        return (user, selectedPlayer);
    }

    private static bool VerifyPassword(string password, string passwordHash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    #endregion

    #region Session

    public async Task JoinAsync(JoinRequest request, CancellationToken cancellationToken)
    {
        var options = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
        var client = await ValidateAccessTokenAsync(request.AccessToken, null, true, cancellationToken);
        var selectedProfileId = YggdrasilFormat.ParseUnsignedUuid(request.SelectedProfile);

        if (client.Player is null || client.Player.Id != selectedProfileId)
        {
            throw YggdrasilException.InvalidToken();
        }

        var key = JoinKey(client.Player.Name, request.ServerId);
        var entry = new JoinSessionEntry(client.Player.Id, request.SelectedProfile, request.ServerId);
        await cacheService.SetAsync(key, entry, TimeSpan.FromSeconds(options.JoinSessionTtlSeconds), cancellationToken);
    }

    public async Task<SessionProfileResponse?> HasJoinedAsync(string username, string serverId, CancellationToken cancellationToken)
    {
        var key = JoinKey(username, serverId);
        var entry = await cacheService.GetAsync<JoinSessionEntry>(key, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        var player = await dbContext.Players
            .AsNoTracking()
            .Include(entity => entity.SkinTexture)
            .Include(entity => entity.CapeTexture)
            .FirstOrDefaultAsync(entity => entity.Id == entry.PlayerId, cancellationToken);
        if (player is null || player.NormalizedName != YggdrasilFormat.Normalize(username))
        {
            return null;
        }

        await cacheService.RemoveAsync(key, cancellationToken);
        return await GetSessionProfileAsync(player.Id, false, cancellationToken);
    }

    private static string JoinKey(string username, string serverId)
    {
        var normalizedName = YggdrasilFormat.Normalize(username);
        var serverHash = YggdrasilFormat.Sha256Hex(serverId);
        return $"yggdrasil:join:{normalizedName}:{serverHash}";
    }

    #endregion

    #region Profile

    public ProfileDto ToProfile(Player player)
    {
        return new ProfileDto(YggdrasilFormat.ToUnsignedId(player.Id), player.Name);
    }

    public YggdrasilUserDto ToUser(User user)
    {
        return new YggdrasilUserDto(
            YggdrasilFormat.ToUnsignedId(user.Id),
            [new UserPropertyDto("preferredLanguage", user.PreferredLanguage)]);
    }

    public async Task<PlayerNameToIdResponse?> FindByNameAsync(string playerName, CancellationToken cancellationToken)
    {
        ValidatePlayerName(playerName);
        var normalizedName = YggdrasilFormat.Normalize(playerName);
        var player = await dbContext.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.NormalizedName == normalizedName, cancellationToken);
        return player is null ? null : new PlayerNameToIdResponse(YggdrasilFormat.ToUnsignedId(player.Id), player.Name);
    }

    public async Task<IReadOnlyCollection<PlayerNameToIdResponse>> FindManyByNameAsync(
        IReadOnlyCollection<string> playerNames,
        CancellationToken cancellationToken)
    {
        var options = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
        if (playerNames.Count == 0 || playerNames.Count > options.MaxProfileLookupCount)
        {
            throw new YggdrasilException(
                StatusCodes.Status400BadRequest,
                "IllegalArgumentException",
                $"The requested profile name count must be between 1 and {options.MaxProfileLookupCount}.");
        }

        var normalizedNames = playerNames.Select(name =>
        {
            ValidatePlayerName(name);
            return YggdrasilFormat.Normalize(name);
        }).Distinct().ToArray();

        return await dbContext.Players
            .AsNoTracking()
            .Where(player => normalizedNames.Contains(player.NormalizedName))
            .Select(player => new PlayerNameToIdResponse(YggdrasilFormat.ToUnsignedId(player.Id), player.Name))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<SessionProfileResponse?> GetSessionProfileAsync(Guid playerId, bool unsigned, CancellationToken cancellationToken)
    {
        var options = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
        var player = await dbContext.Players
            .AsNoTracking()
            .Include(entity => entity.SkinTexture)
            .Include(entity => entity.CapeTexture)
            .FirstOrDefaultAsync(entity => entity.Id == playerId, cancellationToken);

        return player is null ? null : BuildSessionProfile(player, unsigned, options);
    }

    private SessionProfileResponse BuildSessionProfile(Player player, bool unsigned, YggdrasilOptions options)
    {
        var textureMap = new Dictionary<string, object>();
        var skinTextureId = player.SkinTexture?.Id ?? options.DefaultSkinTextureId;
        if (skinTextureId is not null)
        {
            textureMap["SKIN"] = new
            {
                url = TextureUrl(skinTextureId.Value, options),
                metadata = new
                {
                    model = player.SkinModel == SkinModel.Slim ? "slim" : "default"
                }
            };
        }

        if (player.CapeTexture is not null)
        {
            textureMap["CAPE"] = new
            {
                url = TextureUrl(player.CapeTexture.Id, options)
            };
        }

        var texturePayload = new
        {
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            profileId = YggdrasilFormat.ToUnsignedId(player.Id),
            profileName = player.Name,
            textures = textureMap
        };

        var value = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(texturePayload));
        var signature = unsigned ? null : signingKey.SignTextureValue(value);
        return new SessionProfileResponse(
            YggdrasilFormat.ToUnsignedId(player.Id),
            player.Name,
            [new SessionProfileProperty("textures", value, signature)]);
    }

    private static string TextureUrl(Guid textureId, YggdrasilOptions options)
    {
        return $"{options.PublicBaseUrl.TrimEnd('/')}/api/yggdrasil/textures/{textureId:N}.png";
    }

    private static void ValidatePlayerName(string playerName)
    {
        if (!PlayerNameRegex().IsMatch(playerName))
        {
            throw new YggdrasilException(StatusCodes.Status400BadRequest, "IllegalArgumentException", "Invalid profile name.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_]{1,16}$")]
    private static partial Regex PlayerNameRegex();

    #endregion

    #region Texture

    public Task<Texture?> GetTextureAsync(Guid textureId, CancellationToken cancellationToken)
    {
        return dbContext.Textures
            .AsNoTracking()
            .FirstOrDefaultAsync(texture => texture.Id == textureId, cancellationToken);
    }

    #endregion

    #region Metadata

    public async Task<object> GetMetadataAsync(CancellationToken cancellationToken)
    {
        var value = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
        var meta = new Dictionary<string, object?>
        {
            ["serverName"] = value.ServerName,
            ["implementationName"] = value.ImplementationName,
            ["implementationVersion"] = value.ImplementationVersion,
            ["feature.non_email_login"] = value.NonEmailLogin,
            ["feature.legacy_skin_api"] = value.LegacySkinApi,
            ["feature.no_mojang_namespace"] = value.NoMojangNamespace,
            ["feature.enable_mojang_anti_features"] = value.EnableMojangAntiFeatures,
            ["feature.enable_profile_key"] = value.EnableProfileKey,
            ["feature.username_check"] = value.UsernameCheck
        };

        if (!string.IsNullOrWhiteSpace(value.HomepageUrl) || !string.IsNullOrWhiteSpace(value.RegisterUrl))
        {
            meta["links"] = new
            {
                homepage = value.HomepageUrl,
                register = value.RegisterUrl
            };
        }

        return new
        {
            meta,
            skinDomains = value.SkinDomains,
            signaturePublickey = signingKey.PublicKeyPem
        };
    }

    #endregion

    #region Token

    private string CreateAccessToken(YggdrasilClient client, YggdrasilOptions options)
    {
        var now = DateTimeOffset.UtcNow;
        var header = new Dictionary<string, object>
        {
            ["alg"] = "RS256",
            ["typ"] = "JWT"
        };
        var payload = new Dictionary<string, object>
        {
            ["iss"] = options.ImplementationName,
            ["sub"] = client.Id.ToString("D"),
            ["version"] = client.Version,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddHours(options.AccessTokenExpireHours).ToUnixTimeSeconds()
        };

        var headerSegment = YggdrasilFormat.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadSegment = YggdrasilFormat.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = $"{headerSegment}.{payloadSegment}";
        var signature = signingKey.SignJwtInput(Encoding.ASCII.GetBytes(signingInput));
        return $"{signingInput}.{YggdrasilFormat.Base64UrlEncode(signature)}";
    }

    #region Admin

    public async Task<AdminSessionOverviewDto> GetSessionOverviewAsync(
        int recentCount,
        CancellationToken cancellationToken)
    {
        recentCount = Math.Clamp(recentCount, 1, 50);
        var totalClients = await dbContext.YggdrasilClients.CountAsync(cancellationToken);
        var recentClients = await dbContext.YggdrasilClients
            .AsNoTracking()
            .Include(client => client.User)
            .Include(client => client.Player)
            .OrderByDescending(client => client.LastUsedAt)
            .Take(recentCount)
            .ToArrayAsync(cancellationToken);

        var recentActivities = recentClients
            .Select(client => new AdminRecentActivityDto(
                client.Id,
                client.UserId,
                client.User?.UserName ?? string.Empty,
                client.PlayerId,
                client.Player?.Name,
                client.LastUsedAt))
            .ToArray();
        return new AdminSessionOverviewDto(totalClients, recentActivities);
    }

    public async Task RevokeAllUserSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var userExists = await dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken);
        if (!userExists)
        {
            throw new AuthException(StatusCodes.Status404NotFound, "User was not found.");
        }

        await dbContext.YggdrasilClients
            .Where(client => client.UserId == userId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(client => client.Version, client => client.Version + 1),
                cancellationToken);
    }

    public async Task<IReadOnlyCollection<AdminSessionDto>> ListUserSessionsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var userExists = await dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken);
        if (!userExists)
        {
            throw new AuthException(StatusCodes.Status404NotFound, "User was not found.");
        }

        var clients = await dbContext.YggdrasilClients
            .AsNoTracking()
            .Include(client => client.Player)
            .Where(client => client.UserId == userId)
            .OrderByDescending(client => client.LastUsedAt)
            .ToArrayAsync(cancellationToken);

        return clients
            .Select(client => new AdminSessionDto(
                client.Id,
                client.ClientToken,
                client.Version,
                client.PlayerId,
                client.Player?.Name,
                client.AuthenticationMethod,
                client.CreatedAt,
                client.LastUsedAt))
            .ToArray();
    }

    #endregion

    private async Task<YggdrasilClient> ValidateAccessTokenAsync(
        string accessToken,
        string? clientToken,
        bool requirePlayer,
        CancellationToken cancellationToken)
    {
        var parts = accessToken.Split('.');
        if (parts.Length != 3)
        {
            throw YggdrasilException.InvalidToken();
        }

        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = YggdrasilFormat.Base64UrlDecode(parts[2]);
        if (!signingKey.VerifyJwtInput(signingInput, signature))
        {
            throw YggdrasilException.InvalidToken();
        }

        JsonDocument payload;
        try
        {
            payload = JsonDocument.Parse(YggdrasilFormat.Base64UrlDecode(parts[1]));
        }
        catch (JsonException)
        {
            throw YggdrasilException.InvalidToken();
        }

        using (payload)
        {
            if (!payload.RootElement.TryGetProperty("sub", out var subjectElement)
                || !Guid.TryParse(subjectElement.GetString(), out var clientId)
                || !payload.RootElement.TryGetProperty("version", out var versionElement)
                || !payload.RootElement.TryGetProperty("exp", out var expiresElement))
            {
                throw YggdrasilException.InvalidToken();
            }

            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresElement.GetInt64());
            if (DateTimeOffset.UtcNow >= expiresAt)
            {
                throw YggdrasilException.InvalidToken();
            }

            var client = await dbContext.YggdrasilClients
                .Include(entity => entity.User)
                .ThenInclude(user => user.Players)
                .Include(entity => entity.Player)
                .ThenInclude(player => player!.SkinTexture)
                .Include(entity => entity.Player)
                .ThenInclude(player => player!.CapeTexture)
                .FirstOrDefaultAsync(entity => entity.Id == clientId, cancellationToken);

            if (client is null
                || client.User.IsDisabled
                || client.Version != versionElement.GetInt32()
                || (clientToken is not null && client.ClientToken != clientToken)
                || (requirePlayer && client.Player is null))
            {
                throw YggdrasilException.InvalidToken();
            }

            client.LastUsedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return client;
        }
    }

    #endregion
}
