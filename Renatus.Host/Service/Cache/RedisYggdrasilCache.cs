using System.Text.Json;
using StackExchange.Redis;

namespace Renatus.Host.Service.Cache;

public sealed class RedisYggdrasilCache(IConnectionMultiplexer connectionMultiplexer) : IYggdrasilCache
{
    private readonly IDatabase _database = connectionMultiplexer.GetDatabase();

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value);
        await _database.StringSetAsync(key, json, ttl);
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        var value = await _database.StringGetAsync(key);
        if (!value.HasValue)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(value!);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        await _database.KeyDeleteAsync(key);
    }
}
