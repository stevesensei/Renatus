using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;
using Renatus.Host.Entity;
using Renatus.Host.Service.Cache;
using Renatus.Host.Utils;
using StackExchange.Redis;

namespace Renatus.Host.Service.Union;

public sealed class UnionService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    AppConfigurationService configurationService,
    UnionCenterClient centerClient,
    ICacheService cacheService,
    IConnectionMultiplexer connectionMultiplexer,
    YggdrasilSigningKey signingKey,
    ILogger<UnionService> logger)
{
    private static readonly SemaphoreSlim TextureKeyUpdateLock = new(1, 1);
    private readonly IDatabase _redis = connectionMultiplexer.GetDatabase();

    public async Task<UnionMemberInfoResponse> GetMemberInfoAsync(CancellationToken cancellationToken)
    {
        var optionsTask = configurationService.GetUnionOptionsAsync(cancellationToken);
        var stateTask = configurationService.GetUnionStateAsync(cancellationToken);
        await Task.WhenAll(optionsTask, stateTask);
        var options = await optionsTask;
        var state = await stateTask;
        var features = options.Enabled
            ? new[] { "unionBlacklist", "emailVerification" }
            : [];
        var version = typeof(UnionService).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        return new UnionMemberInfoResponse(
            version, state.ServerListVersion, state.PrivateKeyVersion, features);
    }

    public async Task<UnionAdminStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var optionsTask = configurationService.GetUnionOptionsAsync(cancellationToken);
        var stateTask = configurationService.GetUnionStateAsync(cancellationToken);
        await Task.WhenAll(optionsTask, stateTask);
        var options = await optionsTask;
        var state = await stateTask;
        return new UnionAdminStatusResponse(
            options.Enabled,
            state.ServerListVersion,
            state.PrivateKeyVersion,
            state.NeedsProfileSync,
            state.LastProfileSyncAt,
            state.LastProfileSyncError);
    }

    public async Task<UnionAdminSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var options = await configurationService.GetUnionOptionsAsync(cancellationToken);
        return ToSettingsResponse(options);
    }

    public async Task<UnionAdminSettingsResponse> UpdateSettingsAsync(
        UnionAdminUpdateSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RequestTimeoutSeconds is < 1 or > 30)
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "Request timeout must be between 1 and 30 seconds.");
        }

        var apiRoot = request.ApiRoot.Trim();
        var memberCode = request.MemberCode.Trim();
        if (memberCode.Length > 128 || request.MemberKey?.Length > 1024)
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "Union member settings are too long.");
        }

        centerClient.ValidateApiRoot(new UnionOptions { ApiRoot = apiRoot });
        await TextureKeyUpdateLock.WaitAsync(cancellationToken);
        try
        {
            var options = await configurationService.MutateUnionOptionsAsync(current =>
            {
                current.Enabled = request.Enabled;
                current.ApiRoot = apiRoot;
                current.MemberCode = memberCode;
                current.RequestTimeoutSeconds = request.RequestTimeoutSeconds;
                if (request.MemberKey is not null)
                {
                    current.MemberKey = request.MemberKey.Trim();
                }

                return current;
            }, cancellationToken);

            var textureKey = await SelectActiveTextureKeyAsync(options.Enabled, cancellationToken);
            ValidatePrivateKey(textureKey);
            signingKey.InitializeTexture(textureKey);
            return ToSettingsResponse(options);
        }
        finally
        {
            TextureKeyUpdateLock.Release();
        }
    }

    public async Task UpdateMemberKeyAsync(string key, CancellationToken cancellationToken)
    {
        key = key.Trim();
        if (key.Length is 0 or > 1024)
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "Union member key is invalid.");
        }

        await configurationService.MutateUnionOptionsAsync(options =>
        {
            options.MemberKey = key;
            return true;
        }, cancellationToken);
    }

    public async Task UpdateServerListAsync(CancellationToken cancellationToken)
    {
        var options = await configurationService.GetUnionOptionsAsync(cancellationToken);
        var response = await centerClient.GetServerListAsync(options, cancellationToken);
        if (response.Version < 0 || response.Servers.Count > 10_000)
        {
            throw new UnionException(StatusCodes.Status502BadGateway, "Union center returned an invalid server list.");
        }

        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var server in response.Servers)
        {
            if (string.IsNullOrWhiteSpace(server.Code)
                || server.Code.Length > 128
                || !codes.Add(server.Code)
                || !Uri.TryCreate(server.BsRoot, UriKind.Absolute, out var root)
                || (root.Scheme != Uri.UriSchemeHttps && root.Scheme != Uri.UriSchemeHttp))
            {
                throw new UnionException(StatusCodes.Status502BadGateway, "Union center returned an invalid server list.");
            }
        }

        await configurationService.MutateUnionStateAsync(state =>
        {
            state.Servers = response.Servers;
            state.ServerListVersion = response.Version;
            return true;
        }, cancellationToken);
    }

    public async Task UpdatePrivateKeyAsync(CancellationToken cancellationToken)
    {
        var options = await configurationService.GetUnionOptionsAsync(cancellationToken);
        var response = await centerClient.GetPrivateKeyAsync(options, cancellationToken);
        if (response.PrivateKeyVersion < 0)
        {
            throw new UnionException(StatusCodes.Status502BadGateway, "Union center returned an invalid private key version.");
        }

        ValidatePrivateKey(response.PrivateKey);
        await TextureKeyUpdateLock.WaitAsync(cancellationToken);
        try
        {
            await configurationService.MutateUnionStateAsync(state =>
            {
                state.TextureSigningPrivateKeyPem = response.PrivateKey;
                state.PrivateKeyVersion = response.PrivateKeyVersion;
                return true;
            }, cancellationToken);
            var currentOptions = await configurationService.GetUnionOptionsAsync(cancellationToken);
            if (currentOptions.Enabled)
            {
                signingKey.InitializeTexture(response.PrivateKey);
            }
        }
        finally
        {
            TextureKeyUpdateLock.Release();
        }
    }

    public Task MarkProfilesDirtyAsync(CancellationToken cancellationToken)
    {
        return configurationService.MutateUnionStateAsync(state =>
        {
            state.ProfileSyncRevision += 1;
            state.NeedsProfileSync = true;
            return true;
        }, cancellationToken);
    }

    public async Task SynchronizeProfilesAsync(bool force, CancellationToken cancellationToken)
    {
        var options = await configurationService.GetUnionOptionsAsync(cancellationToken);
        if (!options.Enabled)
        {
            return;
        }

        var state = await configurationService.GetUnionStateAsync(cancellationToken);
        var revision = state.ProfileSyncRevision;
        var (profiles, checksum) = await ReadProfilesAsync(cancellationToken);
        if (!force && !state.NeedsProfileSync && state.LastProfileChecksum == checksum)
        {
            return;
        }

        try
        {
            await centerClient.SyncProfilesAsync(options, profiles, cancellationToken);
        }
        catch (UnionException exception)
        {
            await RecordSyncFailureAsync(exception.Message, cancellationToken);
            throw;
        }

        await configurationService.MutateUnionStateAsync(current =>
        {
            current.LastProfileSyncAt = DateTimeOffset.UtcNow;
            current.LastProfileSyncRevision = revision;
            current.LastProfileChecksum = checksum;
            current.LastProfileSyncError = null;
            current.NeedsProfileSync = current.ProfileSyncRevision != revision;
            return true;
        }, cancellationToken);
    }

    public async Task ReconcileProfilesAsync(CancellationToken cancellationToken)
    {
        await SynchronizeProfilesAsync(false, cancellationToken);
    }

    public async Task<UnionEmailResponse?> QueryEmailAsync(
        string? username,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 16)
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "Username is invalid.");
        }

        var normalized = YggdrasilFormat.Normalize(username);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var email = await dbContext.Players
            .AsNoTracking()
            .Where(player => player.NormalizedName == normalized)
            .Select(player => player.User.Email)
            .SingleOrDefaultAsync(cancellationToken);
        return email is null ? null : new UnionEmailResponse(email);
    }

    public async Task ApplyRemapAsync(
        IReadOnlyDictionary<string, string>? remappedUuids,
        CancellationToken cancellationToken)
    {
        if (remappedUuids is null || remappedUuids.Count is 0 or > 1000)
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "UUID remap batch is invalid.");
        }

        var mappings = new Dictionary<Guid, Guid>();
        var targets = new HashSet<Guid>();
        foreach (var (oldValue, newValue) in remappedUuids)
        {
            if (!TryParseUuid(oldValue, out var oldUuid)
                || !TryParseUuid(newValue, out var newUuid)
                || !mappings.TryAdd(oldUuid, newUuid)
                || !targets.Add(newUuid))
            {
                throw new UnionException(StatusCodes.Status400BadRequest, "UUID remap batch is invalid.");
            }
        }

        var relevantUuids = mappings.Keys.Concat(targets).Distinct().ToArray();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var relevantPlayers = await dbContext.Players
            .Where(player => relevantUuids.Contains(player.MinecraftUuid))
            .ToArrayAsync(cancellationToken);
        var byUuid = relevantPlayers.ToDictionary(player => player.MinecraftUuid);
        if (mappings.Keys.Any(uuid => !byUuid.ContainsKey(uuid)))
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "UUID remap source was not found.");
        }

        foreach (var target in targets)
        {
            if (byUuid.ContainsKey(target) && !mappings.ContainsKey(target))
            {
                throw new UnionException(StatusCodes.Status409Conflict, "UUID remap target already exists.");
            }
        }

        var affectedPlayers = mappings.Keys.Select(uuid => byUuid[uuid]).ToArray();
        var temporaryUuids = await CreateTemporaryUuidsAsync(dbContext, affectedPlayers.Length, cancellationToken);
        for (var index = 0; index < affectedPlayers.Length; index++)
        {
            affectedPlayers[index].MinecraftUuid = temporaryUuids[index];
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var player in affectedPlayers)
        {
            var original = mappings.Single(mapping => byUuid[mapping.Key].Id == player.Id);
            player.MinecraftUuid = original.Value;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var playerIds = affectedPlayers.Select(player => player.Id).ToArray();
        await dbContext.YggdrasilClients
            .Where(client => client.PlayerId != null && playerIds.Contains(client.PlayerId.Value))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(client => client.Version, client => client.Version + 1),
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await InvalidateRemappedPlayerCachesAsync(affectedPlayers, cancellationToken);
    }

    public async Task<UnionAdminOperationResponse> DiagnoseCenterAsync(CancellationToken cancellationToken)
    {
        var options = await configurationService.GetUnionOptionsAsync(cancellationToken);
        var result = await centerClient.DiagnoseAsync(options, cancellationToken);
        return result with { Data = null };
    }

    private async Task<string> SelectActiveTextureKeyAsync(
        bool unionEnabled,
        CancellationToken cancellationToken)
    {
        if (unionEnabled)
        {
            var state = await configurationService.GetUnionStateAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(state.TextureSigningPrivateKeyPem))
            {
                return state.TextureSigningPrivateKeyPem;
            }
        }

        var yggdrasil = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
        return yggdrasil.TextureSigningPrivateKeyPem;
    }

    private async Task<(Dictionary<string, string> Profiles, string Checksum)> ReadProfilesAsync(
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await dbContext.Players
            .AsNoTracking()
            .OrderBy(player => player.MinecraftUuid)
            .Select(player => new { player.MinecraftUuid, player.Name })
            .ToArrayAsync(cancellationToken);
        var profiles = rows.ToDictionary(
            row => row.MinecraftUuid.ToString("N"),
            row => row.Name,
            StringComparer.Ordinal);
        var canonical = string.Join('\n', rows.Select(row => $"{row.MinecraftUuid:N}\0{row.Name}"));
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return (profiles, checksum);
    }

    private async Task RecordSyncFailureAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await configurationService.MutateUnionStateAsync(state =>
            {
                state.NeedsProfileSync = true;
                state.LastProfileSyncError = message.Length <= 256 ? message : message[..256];
                return true;
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to persist Union synchronization failure state.");
        }
    }

    private async Task<Guid[]> CreateTemporaryUuidsAsync(
        AppDbContext dbContext,
        int count,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var values = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
            if (values.Distinct().Count() == count
                && !await dbContext.Players.AnyAsync(
                    player => values.Contains(player.MinecraftUuid), cancellationToken))
            {
                return values;
            }
        }
    }

    private async Task InvalidateRemappedPlayerCachesAsync(
        IEnumerable<Player> players,
        CancellationToken cancellationToken)
    {
        var values = players.ToArray();
        foreach (var player in values)
        {
            await cacheService.RemoveAsync(PlayerService.PlayerCacheKey(player.UserId, player.Id), cancellationToken);
        }

        foreach (var userId in values.Select(player => player.UserId).Distinct())
        {
            await cacheService.RemoveAsync(PlayerService.UserPlayersCacheKey(userId), cancellationToken);
        }

        try
        {
            foreach (var endpoint in connectionMultiplexer.GetEndPoints(configuredOnly: true))
            {
                var server = connectionMultiplexer.GetServer(endpoint);
                if (!server.IsConnected)
                {
                    continue;
                }

                foreach (var normalizedName in values.Select(player => player.NormalizedName).Distinct())
                {
                    var keys = server.Keys(
                        _redis.Database, $"yggdrasil:join:{normalizedName}:*", pageSize: 100).ToArray();
                    if (keys.Length > 0)
                    {
                        await _redis.KeyDeleteAsync(keys);
                    }
                }
            }
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Unable to invalidate Union remap join caches.");
        }
    }

    private static UnionAdminSettingsResponse ToSettingsResponse(UnionOptions options)
    {
        return new UnionAdminSettingsResponse(
            options.Enabled,
            options.ApiRoot,
            options.MemberCode,
            !string.IsNullOrWhiteSpace(options.MemberKey),
            options.RequestTimeoutSeconds);
    }

    private static bool TryParseUuid(string value, out Guid uuid)
    {
        return Guid.TryParseExact(value, "N", out uuid) || Guid.TryParseExact(value, "D", out uuid);
    }

    private static void ValidatePrivateKey(string pem)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            _ = rsa.ExportParameters(true);
            if (rsa.KeySize < 2048)
            {
                throw new CryptographicException("RSA key is too small.");
            }
        }
        catch (CryptographicException exception)
        {
            throw new UnionException(StatusCodes.Status502BadGateway, "Union texture signing key is invalid.", exception);
        }
    }
}
