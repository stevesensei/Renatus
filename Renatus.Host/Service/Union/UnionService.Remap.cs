using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Entity;
using Renatus.Host.Utils;
using StackExchange.Redis;

namespace Renatus.Host.Service.Union;

public sealed partial class UnionService
{
    /// <summary>Atomically remaps UUIDs through temporary values without changing REST IDs/FKs. Locks accounts, all their clients (including future refresh bindings), then players in database ID order; ownership/UUID changes reject the batch for caller retry. Bound clients are revoked before commit.</summary>
    public async Task ApplyRemapAsync(IReadOnlyDictionary<string, string>? remappedUuids, CancellationToken cancellationToken)
    {
        if (remappedUuids is null || remappedUuids.Count is 0 or > 1000)
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "UUID remap batch is invalid.");
        }
        var mappings = new Dictionary<Guid, Guid>();
        var targets = new HashSet<Guid>();
        foreach (var (oldValue, newValue) in remappedUuids)
        {
            if (!TryParseUuid(oldValue, out var oldUuid) || !TryParseUuid(newValue, out var newUuid)
                || oldUuid == Guid.Empty || newUuid == Guid.Empty
                || !mappings.TryAdd(oldUuid, newUuid) || !targets.Add(newUuid))
            {
                throw new UnionException(StatusCodes.Status400BadRequest, "UUID remap batch is invalid.");
            }
        }
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        Player[] affectedPlayers;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                var relevantUuids = mappings.Keys.Concat(targets).Distinct().ToArray();
                var snapshot = await dbContext.Players.AsNoTracking()
                    .Where(player => relevantUuids.Contains(player.MinecraftUuid))
                    .Select(player => new { player.Id, player.UserId, player.MinecraftUuid })
                    .ToArrayAsync(cancellationToken);
                var snapshotPlayerIds = snapshot.Select(player => player.Id).ToArray();
                var clientOwnerIds = await dbContext.YggdrasilClients.AsNoTracking()
                    .Where(client => client.PlayerId != null && snapshotPlayerIds.Contains(client.PlayerId.Value))
                    .Select(client => client.UserId).Distinct().ToArrayAsync(cancellationToken);
                var userIds = snapshot.Select(player => player.UserId).Concat(clientOwnerIds).Distinct().ToArray();
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM \"Users\" WHERE \"Id\" = ANY ({userIds}) ORDER BY \"Id\" FOR UPDATE", cancellationToken);
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM \"YggdrasilClients\" WHERE \"UserId\" = ANY ({userIds}) ORDER BY \"Id\" FOR UPDATE", cancellationToken);
                var relevantPlayers = await dbContext.Players.FromSqlInterpolated(
                    $"SELECT * FROM \"Players\" WHERE \"MinecraftUuid\" = ANY ({relevantUuids}) ORDER BY \"Id\" FOR UPDATE")
                    .ToArrayAsync(cancellationToken);
                if (relevantPlayers.Length != snapshot.Length || relevantPlayers.Any(player => !snapshot.Any(
                        prior => prior.Id == player.Id && prior.UserId == player.UserId && prior.MinecraftUuid == player.MinecraftUuid)))
                    throw new UnionException(StatusCodes.Status409Conflict, "UUID remap profiles changed; retry the complete batch.");
                var byUuid = relevantPlayers.ToDictionary(player => player.MinecraftUuid);
                if (mappings.Keys.Any(uuid => !byUuid.ContainsKey(uuid)))
                {
                    throw new UnionException(StatusCodes.Status400BadRequest, "UUID remap source was not found.");
                }
                if (targets.Any(uuid => byUuid.ContainsKey(uuid) && !mappings.ContainsKey(uuid)))
                {
                    throw new UnionException(StatusCodes.Status409Conflict, "UUID remap target already exists.");
                }
                affectedPlayers = mappings.Keys.Select(uuid => byUuid[uuid]).ToArray();
                var finalUuids = mappings.Values.ToArray();
                var temporaryUuids = await CreateTemporaryUuidsAsync(dbContext, affectedPlayers.Length, relevantUuids, cancellationToken);
                for (var index = 0; index < affectedPlayers.Length; index++)
                {
                    affectedPlayers[index].MinecraftUuid = temporaryUuids[index];
                }
                await dbContext.SaveChangesAsync(cancellationToken);
                for (var index = 0; index < affectedPlayers.Length; index++)
                {
                    affectedPlayers[index].MinecraftUuid = finalUuids[index];
                }
                await dbContext.SaveChangesAsync(cancellationToken);
                var playerIds = affectedPlayers.Select(player => player.Id).ToArray();
                await dbContext.YggdrasilClients.Where(client => client.PlayerId != null && playerIds.Contains(client.PlayerId.Value))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(client => client.Version, client => client.Version + 1)
                        .SetProperty(client => client.LastUsedAt, DateTimeOffset.UnixEpoch), cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                throw new UnionException(StatusCodes.Status409Conflict, "UUID remap conflicted with another player update; retry the complete batch.", exception);
            }
        }
        await MarkProfilesDirtyAsync(cancellationToken);
        await InvalidateRemappedPlayerCachesAsync(affectedPlayers, cancellationToken);
    }

    /// <summary>Allocates temporary UUIDs disjoint from all old/new UUIDs and existing rows so immediate unique indexes permit swaps.</summary>
    private static async Task<Guid[]> CreateTemporaryUuidsAsync(AppDbContext dbContext, int count, Guid[] excluded, CancellationToken cancellationToken)
    {
        while (true)
        {
            var values = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
            if (values.Distinct().Count() == count && !values.Intersect(excluded).Any()
                && !await dbContext.Players.AnyAsync(player => values.Contains(player.MinecraftUuid), cancellationToken))
            {
                return values;
            }
        }
    }

    /// <summary>Removes only affected frontend entries and escaped name-specific join keys after commit; Redis outages do not roll back UUID changes or DB token revocation.</summary>
    private async Task InvalidateRemappedPlayerCachesAsync(IEnumerable<Player> players, CancellationToken cancellationToken)
    {
        var values = players.ToArray();
        try
        {
            foreach (var player in values)
            {
                await cacheService.RemoveAsync(PlayerService.PlayerCacheKey(player.UserId, player.Id), cancellationToken);
            }
            foreach (var userId in values.Select(player => player.UserId).Distinct())
            {
                await cacheService.RemoveAsync(PlayerService.UserPlayersCacheKey(userId), cancellationToken);
            }
            foreach (var endpoint in connectionMultiplexer.GetEndPoints(configuredOnly: true))
            {
                var server = connectionMultiplexer.GetServer(endpoint);
                if (!server.IsConnected)
                {
                    continue;
                }
                foreach (var normalizedName in values.Select(player => player.NormalizedName).Distinct())
                {
                    await foreach (var key in server.KeysAsync(_redis.Database, $"yggdrasil:join:{EscapeRedisPattern(normalizedName)}:*", pageSize: 100))
                    {
                        await _redis.KeyDeleteAsync(key);
                    }
                }
            }
        }
        catch (RedisException)
        {
            logger.LogWarning("Union remap cache invalidation failed; database UUID and token-version checks remain authoritative.");
        }
    }

    private static string EscapeRedisPattern(string value)
    {
        return value.Replace("\\", "\\\\").Replace("*", "\\*").Replace("?", "\\?").Replace("[", "\\[").Replace("]", "\\]");
    }

    private static bool TryParseUuid(string value, out Guid uuid)
    {
        return Guid.TryParseExact(value, "N", out uuid) || Guid.TryParseExact(value, "D", out uuid);
    }
}


