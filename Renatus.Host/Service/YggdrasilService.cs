using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;
using Renatus.Host.Entity;
using Renatus.Host.Service.Cache;
using Renatus.Host.Utils;
using StackExchange.Redis;

namespace Renatus.Host.Service;

public sealed partial class YggdrasilService(
    AppDbContext dbContext,
    YggdrasilSigningKey signingKey,
    ICacheService cacheService,
    AppConfigurationService configurationService,
    YggdrasilLifecycleService lifecycleService,
    TextureService textureService,
    PlayerService playerService,
    IConnectionMultiplexer redis,
    YggdrasilConnectOptions startupConnectOptions,
    ILogger<YggdrasilService> logger)
{
    private readonly IDatabase _database = redis.GetDatabase();
    #region Auth

    /// <summary>Issues local RS256 tokens under the initially resolved account lock; rechecks credentials and account identity so a transferred player alias cannot switch owners while waiting and bypass session/revocation serialization. Redis NX bounds login/signout frequency.</summary>
    public async Task<AuthenticateResponse> AuthenticateAsync(AuthenticateRequest request, CancellationToken cancellationToken)
    {
        var options = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
        EnsureAuthServerEnabled(options);
        var (user, selectedPlayer) = await AuthenticateUserAsync(request.Username, request.Password, cancellationToken, "登录初次校验");
        if (user.Players.Count == 0)
        {
            throw new YggdrasilException(
                StatusCodes.Status403Forbidden, "ForbiddenOperationException", "User has no available profiles.");
        }

        await ApplyAuthenticationRateLimitAsync(user.Id, options);
        var lockedUserId = user.Id;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await lifecycleService.LockUserAsync(dbContext, lockedUserId, cancellationToken);
        dbContext.ChangeTracker.Clear();
        (user, selectedPlayer) = await AuthenticateUserAsync(request.Username, request.Password, cancellationToken, "登录加锁后复核");
        if (user.Id != lockedUserId)
        {
            logger.LogWarning(
                "Yggdrasil 登录失败：加锁后账户身份发生变化。登录标识={Login}，原账户ID={LockedUserId}，当前账户ID={UserId}，角色ID={PlayerId}，追踪ID={TraceId}",
                JsonSerializer.Serialize(request.Username), lockedUserId, user.Id, selectedPlayer?.Id, Activity.Current?.Id);
            throw YggdrasilException.InvalidCredentials();
        }
        var clientToken = string.IsNullOrWhiteSpace(request.ClientToken)
            ? Guid.NewGuid().ToString("N")
            : request.ClientToken;
        if (clientToken.Length > 128)
        {
            throw new YggdrasilException(400, "IllegalArgumentException", "Client token is too long.");
        }

        var client = await dbContext.YggdrasilClients
            .FirstOrDefaultAsync(entity => entity.UserId == user.Id && entity.ClientToken == clientToken, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var activeSince = now.AddHours(-options.RefreshTokenExpireHours);
        if (client is null || client.LastUsedAt < activeSince)
        {
            var activeSessions = await dbContext.YggdrasilClients.CountAsync(
                entity => entity.UserId == user.Id && entity.LastUsedAt >= activeSince,
                cancellationToken);
            if (activeSessions >= options.MaxActiveSessions)
            {
                throw new YggdrasilException(
                    StatusCodes.Status403Forbidden,
                    "ForbiddenOperationException",
                    "The active session limit has been reached.");
            }
        }

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
            var version = client.Version;
            var changed = await dbContext.YggdrasilClients
                .Where(entity => entity.Id == client.Id && entity.Version == version)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(entity => entity.Version, entity => entity.Version + 1)
                    .SetProperty(entity => entity.PlayerId, selectedPlayer == null ? (Guid?)null : selectedPlayer.Id)
                    .SetProperty(entity => entity.AuthenticationMethod, AuthenticationMethodType.Password)
                    .SetProperty(entity => entity.LastUsedAt, now), cancellationToken);
            if (changed != 1) throw YggdrasilException.InvalidToken();
            client.Version += 1;
            client.PlayerId = selectedPlayer?.Id;
            client.AuthenticationMethod = AuthenticationMethodType.Password;
            client.LastUsedAt = now;
            // ExecuteUpdate bypasses tracking; Unchanged would otherwise restore the stale original Version.
            dbContext.Entry(client).OriginalValues.SetValues(client);
            dbContext.Entry(client).State = EntityState.Unchanged;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var accessToken = await CreateAccessTokenAsync(client, options, cancellationToken);
        var selectedProfile = selectedPlayer is null ? null : ToProfile(selectedPlayer);
        var availableProfiles = selectedProfile is null
            ? user.Players.Select(ToProfile).ToArray()
            : [selectedProfile];
        return new AuthenticateResponse(
            accessToken,
            client.ClientToken,
            selectedProfile,
            availableProfiles,
            request.RequestUser ? ToUser(user) : null);
    }

    /// <summary>Allows refresh-window tokens and rotates Version under the account lock. Revalidation after waiting prevents an unbound client from using an ownership snapshot predating transfer/remap; committed tracked originals preserve the rotated version.</summary>
    public async Task<RefreshResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var options = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
        EnsureAuthServerEnabled(options);
        var validated = await ValidateAccessTokenAsync(
            request.AccessToken, request.ClientToken, false, true, cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await lifecycleService.LockUserAsync(dbContext, validated.Client.UserId, cancellationToken);
        dbContext.ChangeTracker.Clear();
        validated = await ValidateAccessTokenAsync(
            request.AccessToken, request.ClientToken, false, true, cancellationToken);
        var client = validated.Client;
        Player? selectedPlayer = client.Player;

        if (request.SelectedProfile is not null)
        {
            var selectedProfileId = YggdrasilFormat.ParseUnsignedUuid(request.SelectedProfile.Id);
            if (client.PlayerId is not null)
            {
                if (selectedProfileId != client.Player!.MinecraftUuid)
                    throw new YggdrasilException(
                    StatusCodes.Status400BadRequest,
                    "IllegalArgumentException",
                    "Access token already has a profile assigned.");
            }

            selectedPlayer = client.User.Players.FirstOrDefault(player => player.MinecraftUuid == selectedProfileId);
            if (selectedPlayer is null)
            {
                throw YggdrasilException.InvalidToken();
            }

            client.PlayerId = selectedPlayer.Id;
        }

        if (selectedPlayer is null)
        {
            throw new YggdrasilException(
                StatusCodes.Status400BadRequest,
                "IllegalArgumentException",
                "A selected profile is required.");
        }

        var oldVersion = client.Version;
        var lastUsedAt = DateTimeOffset.UtcNow;
        var changed = await dbContext.YggdrasilClients
            .Where(entity => entity.Id == client.Id && entity.Version == oldVersion && !entity.User.IsDisabled)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(entity => entity.Version, entity => entity.Version + 1)
                .SetProperty(entity => entity.PlayerId, selectedPlayer.Id)
                .SetProperty(entity => entity.LastUsedAt, lastUsedAt), cancellationToken);
        if (changed != 1) throw YggdrasilException.InvalidToken();
        client.Version = oldVersion + 1;
        client.Player = selectedPlayer;
        client.PlayerId = selectedPlayer.Id;
        client.LastUsedAt = lastUsedAt;
        // Accept the atomically committed version before Unchanged can reject the tracked changes.
        dbContext.Entry(client).OriginalValues.SetValues(client);
        dbContext.Entry(client).State = EntityState.Unchanged;

        await transaction.CommitAsync(cancellationToken);

        var accessToken = await CreateAccessTokenAsync(client, options, cancellationToken);
        return new RefreshResponse(
            accessToken,
            client.ClientToken,
            ToProfile(selectedPlayer),
            [ToProfile(selectedPlayer)],
            request.RequestUser ? ToUser(client.User) : null);
    }

    /// <summary>Checks the usable lifetime and committed rename boundary; validation never extends the refresh window or session slot lifetime.</summary>
    public async Task ValidateAsync(ValidateRequest request, CancellationToken cancellationToken)
    {
        var options = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
        EnsureAuthServerEnabled(options);
        await ValidateAccessTokenAsync(request.AccessToken, request.ClientToken, false, false, cancellationToken);
    }

    /// <summary>Idempotently hard-revokes a refreshable local token by bumping Version and releasing its active session slot.</summary>
    public async Task InvalidateAsync(InvalidateRequest request, CancellationToken cancellationToken)
    {
        var options = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
        EnsureAuthServerEnabled(options);
        try
        {
            var client = (await ValidateAccessTokenAsync(
                request.AccessToken, request.ClientToken, false, true, cancellationToken)).Client;
            await dbContext.YggdrasilClients.Where(entity => entity.Id == client.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(entity => entity.Version, entity => entity.Version + 1)
                    .SetProperty(entity => entity.LastUsedAt, DateTimeOffset.UnixEpoch), cancellationToken);
        }
        catch (YggdrasilException)
        {
        }
    }

    /// <summary>Rechecks credentials and the initially locked account identity before revoking local clients. A transferred alias is rejected instead of locking a second owner out of order; shares the atomic login/signout rate interval.</summary>
    public async Task SignoutAsync(SignoutRequest request, CancellationToken cancellationToken)
    {
        var options = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
        EnsureAuthServerEnabled(options);
        var (user, _) = await AuthenticateUserAsync(request.Username, request.Password, cancellationToken, "退出初次校验");
        await ApplyAuthenticationRateLimitAsync(user.Id, options);
        var lockedUserId = user.Id;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await lifecycleService.LockUserAsync(dbContext, lockedUserId, cancellationToken);
        dbContext.ChangeTracker.Clear();
        (user, _) = await AuthenticateUserAsync(request.Username, request.Password, cancellationToken, "退出加锁后复核");
        if (user.Id != lockedUserId)
        {
            logger.LogWarning(
                "Yggdrasil 退出失败：加锁后账户身份发生变化。登录标识={Login}，原账户ID={LockedUserId}，当前账户ID={UserId}，追踪ID={TraceId}",
                JsonSerializer.Serialize(request.Username), lockedUserId, user.Id, Activity.Current?.Id);
            throw YggdrasilException.InvalidCredentials();
        }
        await lifecycleService.RevokeUserClientsAsync(dbContext, user.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Resolves normalized account/email/profile names, rejects disabled users and enforces verified email only when Union is enabled.</summary>
    private async Task<(User User, Player? SelectedPlayer)> AuthenticateUserAsync(
        string login,
        string password,
        CancellationToken cancellationToken,
        string stage)
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

        var unionOptions = await configurationService.GetUnionOptionsAsync(cancellationToken);
        var passwordMethod = user?.AuthenticationMethods.FirstOrDefault(method => method.Type == AuthenticationMethodType.Password);
        if (user is null || user.IsDisabled || (unionOptions.Enabled && user.EmailVerifiedAt is null))
        {
            LogCredentialFailure(user is null ? "未找到账户" : user.IsDisabled ? "账户已禁用" : "Union 已启用但账户邮箱未验证");
            throw YggdrasilException.InvalidCredentials();
        }

        if (passwordMethod?.PasswordHash is null)
        {
            LogCredentialFailure(passwordMethod is null ? "账户未配置密码登录方式" : "密码登录方式缺少密码哈希");
            throw YggdrasilException.InvalidCredentials();
        }

        try
        {
            if (!BCrypt.Net.BCrypt.Verify(password, passwordMethod.PasswordHash))
            {
                LogCredentialFailure("BCrypt 密码校验不匹配");
                throw YggdrasilException.InvalidCredentials();
            }
        }
        catch (BCrypt.Net.SaltParseException)
        {
            LogCredentialFailure("BCrypt 密码哈希格式无效或为不兼容的旧格式");
            throw YggdrasilException.InvalidCredentials();
        }

        if (selectedPlayer is null && user.Players.Count == 1)
        {
            selectedPlayer = user.Players.Single();
        }

        return (user, selectedPlayer);

        void LogCredentialFailure(string reason)
        {
            var matchSource = selectedPlayer is not null ? "角色名"
                : user is null ? "未匹配"
                : user.NormalizedUserName == normalizedLogin ? "用户名" : "邮箱";
            logger.LogWarning(
                "Yggdrasil 凭据校验失败：{Reason}。阶段={Stage}，追踪ID={TraceId}，登录标识={Login}，规范化标识={NormalizedLogin}，匹配方式={MatchSource}，"
                + "账户ID={UserId}，用户名={UserName}，用户名匹配={UserNameMatches}，邮箱匹配={EmailMatches}，账户已禁用={IsDisabled}，"
                + "Union已启用={UnionEnabled}，邮箱验证时间={EmailVerifiedAt}，角色数量={PlayerCount}，角色ID={PlayerId}，角色名={PlayerName}，MinecraftUUID={MinecraftUuid}，"
                + "认证方式={AuthenticationMethods}，密码认证ID={PasswordMethodId}，存在密码哈希={HasPasswordHash}，密码哈希长度={PasswordHashLength}，输入密码长度={PasswordLength}",
                reason, stage, Activity.Current?.Id, JsonSerializer.Serialize(login), JsonSerializer.Serialize(normalizedLogin), matchSource,
                user?.Id, user?.UserName, user?.NormalizedUserName == normalizedLogin, user?.NormalizedEmail == normalizedLogin, user?.IsDisabled,
                unionOptions.Enabled, user?.EmailVerifiedAt, user?.Players.Count, selectedPlayer?.Id, selectedPlayer?.Name, selectedPlayer?.MinecraftUuid,
                user is null ? string.Empty : string.Join(",", user.AuthenticationMethods.Select(method => method.Type)),
                passwordMethod?.Id, passwordMethod?.PasswordHash is not null, passwordMethod?.PasswordHash?.Length, password.Length);
        }
    }

    #endregion

    #region Session

    /// <summary>Validates a usable local Yggdrasil token bound to the Minecraft UUID and caches internal identity, version and expiry for later database revalidation.</summary>
    public async Task JoinAsync(JoinRequest request, CancellationToken cancellationToken)
    {
        var options = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
        var validated = await ValidateAccessTokenAsync(request.AccessToken, null, true, false, cancellationToken);
        var client = validated.Client;
        var selectedProfileId = YggdrasilFormat.ParseUnsignedUuid(request.SelectedProfile);

        if (client.Player is null || client.Player.MinecraftUuid != selectedProfileId)
        {
            logger.LogInformation("传统 Yggdrasil join 拒绝：选中角色与令牌绑定UUID不符，选中UUID={SelectedUuid}，令牌角色UUID={BoundUuid}，追踪ID={TraceId}",
                selectedProfileId, client.Player?.MinecraftUuid, Activity.Current?.Id);
            throw YggdrasilException.InvalidToken();
        }

        var key = JoinKey(client.Player.Name, request.ServerId);
        var entry = new JoinSessionEntry(
            client.Player.Id,
            client.Player.MinecraftUuid,
            client.Id,
            client.Version,
            validated.IssuedAtMilliseconds,
            validated.ExpiresAtMilliseconds,
            request.SelectedProfile,
            request.ServerId);
        await cacheService.SetAsync(key, entry, TimeSpan.FromSeconds(options.JoinSessionTtlSeconds), cancellationToken);
        logger.LogInformation("传统 Yggdrasil join 已保存：角色名={PlayerName}，UUID={ProfileId}，服务器摘要={ServerHash}，有效期秒={TtlSeconds}，读取方式=一次性GETDEL，追踪ID={TraceId}",
            JsonSerializer.Serialize(client.Player.Name), client.Player.MinecraftUuid,
            YggdrasilFormat.Sha256Hex(request.ServerId), options.JoinSessionTtlSeconds, Activity.Current?.Id);
    }

    /// <summary>使用 GETDEL 一次性读取入服记录，复核数据库状态，并用中文 Info 日志记录未命中或首个拒绝原因。</summary>
    public async Task<SessionProfileResponse?> HasJoinedAsync(string username, string serverId, CancellationToken cancellationToken)
    {
        var key = JoinKey(username, serverId);
        var cached = await _database.StringGetDeleteAsync(key);
        var entry = cached.HasValue ? JsonSerializer.Deserialize<JoinSessionEntry>(cached.ToString()) : null;
        if (entry is null)
        {
            logger.LogInformation("传统 Yggdrasil hasJoined 无登录记录：角色名={PlayerName}，服务器摘要={ServerHash}，缓存存在={CacheHit}；可能尚未join、已过期或已被读取，追踪ID={TraceId}",
                JsonSerializer.Serialize(username), YggdrasilFormat.Sha256Hex(serverId), cached.HasValue, Activity.Current?.Id);
            return null;
        }
        logger.LogInformation("传统 Yggdrasil hasJoined 已读取并删除登录记录：角色UUID={ProfileId}，服务器摘要={ServerHash}，追踪ID={TraceId}",
            entry.MinecraftUuid, YggdrasilFormat.Sha256Hex(serverId), Activity.Current?.Id);

        var player = await dbContext.Players
            .AsNoTracking()
            .Include(entity => entity.SkinTexture)
            .Include(entity => entity.CapeTexture)
            .FirstOrDefaultAsync(entity => entity.Id == entry.PlayerId, cancellationToken);
        var client = await dbContext.YggdrasilClients
            .AsNoTracking()
            .Include(entity => entity.User)
            .FirstOrDefaultAsync(entity => entity.Id == entry.ClientId, cancellationToken);
        var renamedAt = player is null
            ? null
            : await lifecycleService.GetPlayerRenamedAtAsync(dbContext, player.Id, cancellationToken);
        var unionOptions = await configurationService.GetUnionOptionsAsync(cancellationToken);
        string? rejectionReason = null;
        if (player is null) rejectionReason = "角色不存在";
        else if (client is null) rejectionReason = "客户端会话不存在";
        else if (client.User.IsDisabled) rejectionReason = "账户已禁用";
        else if (unionOptions.Enabled && client.User.EmailVerifiedAt is null) rejectionReason = "联盟启用但邮箱未验证";
        else if (client.Version != entry.ClientVersion) rejectionReason = "令牌版本已变更";
        else if (client.UserId != player.UserId) rejectionReason = "角色所有者已变更";
        else if (client.PlayerId != player.Id) rejectionReason = "客户端选中角色已变更";
        else if (player.MinecraftUuid != entry.MinecraftUuid) rejectionReason = "角色UUID已变更";
        else if (YggdrasilFormat.ToUnsignedId(player.MinecraftUuid) != entry.ProfileId) rejectionReason = "选中UUID字符串与当前UUID不符";
        else if (player.NormalizedName != YggdrasilFormat.Normalize(username)) rejectionReason = "查询名称与当前角色名称不符";
        else if (entry.ServerId != serverId) rejectionReason = "服务器标识不符";
        else if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= entry.ExpiresAtMilliseconds) rejectionReason = "访问令牌已过期";
        else if (renamedAt >= entry.IssuedAtMilliseconds) rejectionReason = "角色在令牌签发后改名";
        if (player is null || rejectionReason is not null)
        {
            logger.LogInformation("传统 Yggdrasil hasJoined 拒绝：原因={Reason}，查询角色名={PlayerName}，记录UUID={EntryUuid}，当前UUID={CurrentUuid}，服务器摘要={ServerHash}，追踪ID={TraceId}",
                rejectionReason, JsonSerializer.Serialize(username), entry.MinecraftUuid, player?.MinecraftUuid,
                YggdrasilFormat.Sha256Hex(serverId), Activity.Current?.Id);
            return null;
        }
        logger.LogInformation("传统 Yggdrasil hasJoined 校验通过：UUID={ProfileId}，服务器摘要={ServerHash}，追踪ID={TraceId}",
            entry.MinecraftUuid, YggdrasilFormat.Sha256Hex(serverId), Activity.Current?.Id);

        return BuildSessionProfile(player, false, await configurationService.GetYggdrasilOptionsAsync(cancellationToken));
    }

    private static string JoinKey(string username, string serverId)
    {
        var normalizedName = YggdrasilFormat.Normalize(username);
        var serverHash = YggdrasilFormat.Sha256Hex(serverId);
        return $"yggdrasil:join:{normalizedName}:{serverHash}";
    }

    #endregion

    #region Profile

    /// <summary>Exports the Minecraft UUID; frontend REST continues to use the unrelated internal player primary key.</summary>
    public ProfileDto ToProfile(Player player)
    {
        return new ProfileDto(YggdrasilFormat.ToUnsignedId(player.MinecraftUuid), player.Name);
    }

    public YggdrasilUserDto ToUser(User user)
    {
        return new YggdrasilUserDto(
            YggdrasilFormat.ToUnsignedId(user.Id),
            [new UserPropertyDto("preferredLanguage", user.PreferredLanguage)]);
    }

    /// <summary>Resolves a normalized player name to its persisted Minecraft UUID for both legacy and minecraftservices aliases.</summary>
    public async Task<PlayerNameToIdResponse?> FindByNameAsync(string playerName, CancellationToken cancellationToken)
    {
        ValidatePlayerName(playerName);
        var normalizedName = YggdrasilFormat.Normalize(playerName);
        var player = await dbContext.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.NormalizedName == normalizedName, cancellationToken);
        return player is null
            ? null
            : new PlayerNameToIdResponse(YggdrasilFormat.ToUnsignedId(player.MinecraftUuid), player.Name);
    }

    /// <summary>Bounds and deduplicates normalized profile names while returning persisted Minecraft UUIDs.</summary>
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
            .Select(player => new PlayerNameToIdResponse(
                YggdrasilFormat.ToUnsignedId(player.MinecraftUuid), player.Name))
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>Reads a public session profile by Minecraft UUID, independently of internal REST IDs and bearer authentication.</summary>
    public async Task<SessionProfileResponse?> GetSessionProfileAsync(
        Guid minecraftUuid, bool unsigned, CancellationToken cancellationToken)
    {
        var options = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
        var player = await dbContext.Players
            .AsNoTracking()
            .Include(entity => entity.SkinTexture)
            .Include(entity => entity.CapeTexture)
            .FirstOrDefaultAsync(entity => entity.MinecraftUuid == minecraftUuid, cancellationToken);

        logger.LogInformation("Yggdrasil 资料读取：查询UUID={ProfileId}，角色存在={Found}，需要签名={Signed}，追踪ID={TraceId}",
            minecraftUuid, player is not null, !unsigned, Activity.Current?.Id);
        return player is null ? null : BuildSessionProfile(player, unsigned, options);
    }

    /// <summary>Builds textures and uploadableTextures properties with Minecraft identity; signed responses sign every property using the active texture RSA with SHA-1.</summary>
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
            profileId = YggdrasilFormat.ToUnsignedId(player.MinecraftUuid),
            profileName = player.Name,
            isPublic = true,
            signatureRequired = unsigned ? (bool?)null : true,
            textures = textureMap
        };

        var value = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(texturePayload,
            new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }));
        var uploadableValue = "skin,cape";
        var properties = new[]
        {
            new SessionProfileProperty(
                "textures", value, unsigned ? null : signingKey.SignTextureValue(value)),
            new SessionProfileProperty(
                "uploadableTextures",
                uploadableValue,
                unsigned ? null : signingKey.SignTextureValue(uploadableValue))
        };
        logger.LogInformation("Yggdrasil 角色资料已生成：UUID={ProfileId}，角色名={PlayerName}，已签名={Signed}，材质类型={TextureTypes}，材质站点={TextureBaseUrl}，域名白名单={SkinDomains}，材质公钥摘要={KeyFingerprint}，追踪ID={TraceId}",
            player.MinecraftUuid, JsonSerializer.Serialize(player.Name), !unsigned,
            string.Join(",", textureMap.Keys), options.PublicBaseUrl, string.Join(",", options.SkinDomains),
            YggdrasilFormat.Sha256Hex(signingKey.TexturePublicKeyPem), Activity.Current?.Id);
        return new SessionProfileResponse(
            YggdrasilFormat.ToUnsignedId(player.MinecraftUuid),
            player.Name,
            properties);
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

    /// <summary>Publishes the active texture public key and enabled Union skin domains without exposing the immutable local JWT key or private configuration.</summary>
    public async Task<object> GetMetadataAsync(CancellationToken cancellationToken)
    {
        var value = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
        var connect = startupConnectOptions;
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
        if (connect.Enabled)
            meta["feature.openid_configuration_url"] = $"{connect.Issuer.TrimEnd('/')}/.well-known/openid-configuration";

        if (!string.IsNullOrWhiteSpace(value.HomepageUrl) || !string.IsNullOrWhiteSpace(value.RegisterUrl))
        {
            meta["links"] = new
            {
                homepage = value.HomepageUrl,
                register = value.RegisterUrl
            };
        }

        var texturePublicKey = signingKey.TexturePublicKeyPem;
        logger.LogInformation("Yggdrasil 元数据已生成：域名白名单={SkinDomains}，材质公钥摘要={KeyFingerprint}，追踪ID={TraceId}",
            string.Join(",", value.SkinDomains), YggdrasilFormat.Sha256Hex(texturePublicKey), Activity.Current?.Id);
        return new
        {
            meta,
            skinDomains = value.SkinDomains,
            signaturePublickey = texturePublicKey
        };
    }

    #endregion

    #region Token

    /// <summary>Signs a client/version JWT with the immutable local key; iat_ms is strictly after the latest rename so immediate fresh tokens remain usable.</summary>
    private async Task<string> CreateAccessTokenAsync(YggdrasilClient client, YggdrasilOptions options, CancellationToken cancellationToken)
    {
        var renamedAt = client.PlayerId is null ? null
            : await lifecycleService.GetPlayerRenamedAtAsync(dbContext, client.PlayerId.Value, cancellationToken);
        var issuedAtMilliseconds = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), (renamedAt ?? 0) + 1);
        var now = DateTimeOffset.FromUnixTimeMilliseconds(issuedAtMilliseconds);
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
            ["iat_ms"] = issuedAtMilliseconds,
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

    /// <summary>Hard-revokes all local client versions under the same account lock used by authentication, and releases the session slots.</summary>
    public async Task RevokeAllUserSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var userExists = await dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken);
        if (!userExists)
        {
            throw new AuthException(StatusCodes.Status404NotFound, "User was not found.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await lifecycleService.RevokeUserClientsAsync(dbContext, userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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

    /// <summary>Verifies only local RS256 client tokens, ownership and Version. Usable calls enforce rename/exp; refresh calls use the bounded issuance window, with legacy iat seconds converted to milliseconds.</summary>
    private async Task<ValidatedAccessToken> ValidateAccessTokenAsync(
        string accessToken,
        string? clientToken,
        bool requirePlayer,
        bool allowRefreshWindow,
        CancellationToken cancellationToken)
    {
        try
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

            using var header = JsonDocument.Parse(YggdrasilFormat.Base64UrlDecode(parts[0]));
            if (!header.RootElement.TryGetProperty("alg", out var algorithm) || algorithm.GetString() != "RS256")
                throw YggdrasilException.InvalidToken();

            using var payload = JsonDocument.Parse(YggdrasilFormat.Base64UrlDecode(parts[1]));
            if (!payload.RootElement.TryGetProperty("sub", out var subjectElement)
                || !Guid.TryParse(subjectElement.GetString(), out var clientId)
                || !payload.RootElement.TryGetProperty("version", out var versionElement)
                || !payload.RootElement.TryGetProperty("iat", out var issuedAtElement)
                || !payload.RootElement.TryGetProperty("exp", out var expiresElement))
            {
                throw YggdrasilException.InvalidToken();
            }

            var options = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
            var unionOptions = await configurationService.GetUnionOptionsAsync(cancellationToken);
            var issuedAtSeconds = issuedAtElement.GetInt64();
            var issuedAtMilliseconds = payload.RootElement.TryGetProperty("iat_ms", out var issuedAtMsElement)
                ? issuedAtMsElement.GetInt64()
                : checked(issuedAtSeconds * 1000);
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresElement.GetInt64());
            var refreshExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds(issuedAtMilliseconds)
                .AddHours(options.RefreshTokenExpireHours);
            if (DateTimeOffset.UtcNow >= (allowRefreshWindow ? refreshExpiresAt : expiresAt))
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
                || (unionOptions.Enabled && client.User.EmailVerifiedAt is null)
                || client.Version != versionElement.GetInt32()
                || (clientToken is not null && client.ClientToken != clientToken)
                || (requirePlayer && client.Player is null)
                || (client.Player is not null && client.Player.UserId != client.UserId))
            {
                throw YggdrasilException.InvalidToken();
            }

            if (!allowRefreshWindow && client.Player is not null)
            {
                var renamedAt = await lifecycleService.GetPlayerRenamedAtAsync(dbContext, client.Player.Id, cancellationToken);
                if (renamedAt >= issuedAtMilliseconds)
                {
                    throw YggdrasilException.InvalidToken();
                }
            }

            return new ValidatedAccessToken(client, issuedAtMilliseconds, expiresAt.ToUnixTimeMilliseconds());
        }
        catch (Exception exception) when (exception is FormatException
                                           or JsonException
                                           or InvalidOperationException
                                           or ArgumentOutOfRangeException
                                           or OverflowException
                                           or System.Security.Cryptography.CryptographicException)
        {
            throw YggdrasilException.InvalidToken();
        }
    }

    /// <summary>Applies the authserver switch to password/session management endpoints without disabling public session profile lookup.</summary>
    private static void EnsureAuthServerEnabled(YggdrasilOptions options)
    {
        if (!options.AuthServerEnabled)
        {
            throw new YggdrasilException(
                StatusCodes.Status403Forbidden,
                "ForbiddenOperationException",
                "The authentication server is disabled.");
        }
    }

    /// <summary>Uses a single atomic Redis NX write and TTL per account to bound concurrent login/signout requests across processes.</summary>
    private async Task ApplyAuthenticationRateLimitAsync(Guid userId, YggdrasilOptions options)
    {
        var accepted = await _database.StringSetAsync(
            $"yggdrasil:auth-rate:{userId:N}",
            "1",
            TimeSpan.FromMilliseconds(options.AuthenticationRateLimitMilliseconds),
            When.NotExists);
        if (!accepted)
        {
            throw new YggdrasilException(
                StatusCodes.Status403Forbidden,
                "ForbiddenOperationException",
                "Authentication requests are too frequent.");
        }
    }

    private sealed record ValidatedAccessToken(YggdrasilClient Client, long IssuedAtMilliseconds, long ExpiresAtMilliseconds);

    #endregion
}
