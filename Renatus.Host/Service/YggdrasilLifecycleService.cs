using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Entity;

namespace Renatus.Host.Service;

public sealed class YggdrasilLifecycleService
{
    /// <summary>Locks the account before changing credentials or issuing sessions, so a concurrent login cannot bypass hard revocation.</summary>
    public async Task LockUserAsync(AppDbContext dbContext, Guid userId, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE", cancellationToken);
    }

    /// <summary>Locks all involved accounts in PostgreSQL ID order before clients or players; transfers lock both owners so authentication cannot create a session during ownership changes.</summary>
    public async Task LockUsersAsync(AppDbContext dbContext, IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToArray();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Users\" WHERE \"Id\" = ANY ({ids}) ORDER BY \"Id\" FOR UPDATE", cancellationToken);
    }

    /// <summary>Locks the account then its clients in database ID order and revokes usable/refreshable tokens in the caller's credential-change transaction.</summary>
    public async Task<int> RevokeUserClientsAsync(
        AppDbContext dbContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await LockUserAsync(dbContext, userId, cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"YggdrasilClients\" WHERE \"UserId\" = {userId} ORDER BY \"Id\" FOR UPDATE", cancellationToken);
        return await dbContext.YggdrasilClients
            .Where(client => client.UserId == userId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(client => client.Version, client => client.Version + 1)
                    .SetProperty(client => client.LastUsedAt, DateTimeOffset.UnixEpoch),
                cancellationToken);
    }

    /// <summary>After the caller locks the affected accounts, locks bound clients in PostgreSQL ID order before player mutation and revokes their versions; join snapshots also check the version.</summary>
    public async Task<int> RevokePlayerClientsAsync(
        AppDbContext dbContext,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"YggdrasilClients\" WHERE \"PlayerId\" = {playerId} ORDER BY \"Id\" FOR UPDATE", cancellationToken);
        return await dbContext.YggdrasilClients
            .Where(client => client.PlayerId == playerId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(client => client.Version, client => client.Version + 1)
                    .SetProperty(client => client.LastUsedAt, DateTimeOffset.UnixEpoch),
                cancellationToken);
    }

    /// <summary>Records a soft rename in the same database transaction as the player change without revoking the refresh window.</summary>
    public void MarkPlayerRenamed(AppDbContext dbContext, Player player)
    {
        dbContext.YggdrasilActivities.Add(new YggdrasilActivity
        {
            Id = Guid.NewGuid(), Action = "profile.rename", PlayerId = player.Id, UserId = player.UserId
        });
    }

    /// <summary>Reads the committed rename boundary using the internal player key; usable tokens at or before it fail while refresh remains allowed.</summary>
    public async Task<long?> GetPlayerRenamedAtAsync(AppDbContext dbContext, Guid playerId, CancellationToken cancellationToken)
    {
        var renamedAt = await dbContext.YggdrasilActivities.AsNoTracking()
            .Where(activity => activity.PlayerId == playerId && activity.Action == "profile.rename")
            .Select(activity => (DateTimeOffset?)activity.CreatedAt).MaxAsync(cancellationToken);
        return renamedAt?.ToUnixTimeMilliseconds();
    }
}
