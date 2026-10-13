using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using StackExchange.Redis;

namespace Renatus.Host.Service;

public sealed class YggdrasilLifecycleService(
    IConnectionMultiplexer redis,
    AppConfigurationService configurationService)
{
    private readonly IDatabase _database = redis.GetDatabase();

    public Task<int> RevokeUserClientsAsync(
        AppDbContext dbContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return dbContext.YggdrasilClients
            .Where(client => client.UserId == userId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(client => client.Version, client => client.Version + 1),
                cancellationToken);
    }

    public Task<int> RevokePlayerClientsAsync(
        AppDbContext dbContext,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        return dbContext.YggdrasilClients
            .Where(client => client.PlayerId == playerId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(client => client.Version, client => client.Version + 1),
                cancellationToken);
    }

    public async Task MarkPlayerRenamedAsync(Guid minecraftUuid, CancellationToken cancellationToken)
    {
        var options = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
        await _database.StringSetAsync(
            RenameKey(minecraftUuid),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TimeSpan.FromHours(options.RefreshTokenExpireHours));
    }

    public async Task<long?> GetPlayerRenamedAtAsync(Guid minecraftUuid)
    {
        var value = await _database.StringGetAsync(RenameKey(minecraftUuid));
        return value.TryParse(out long milliseconds) ? milliseconds : null;
    }

    private static string RenameKey(Guid minecraftUuid)
    {
        return $"yggdrasil:player-renamed:{minecraftUuid:N}";
    }
}
