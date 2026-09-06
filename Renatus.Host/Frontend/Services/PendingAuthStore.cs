using Microsoft.Extensions.Caching.Memory;
using Renatus.Host.Data;

namespace Renatus.Host.Frontend.Services;

public sealed class PendingAuthStore(IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public string Stage(AuthResponse response)
    {
        var key = Guid.NewGuid().ToString("N");
        cache.Set(key, response, Ttl);
        return key;
    }

    public AuthResponse? Consume(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var response = cache.Get<AuthResponse>(key);
        cache.Remove(key);
        return response;
    }
}
