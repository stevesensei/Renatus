namespace Renatus.Host.Service.Cache;

internal static class RegistrationVerificationScripts
{
    public const string AcquireSend = """
        if redis.call('EXISTS', KEYS[2]) == 1 then return -1 end
        local count = tonumber(redis.call('GET', KEYS[1]) or '0')
        if count >= tonumber(ARGV[1]) then return -2 end
        count = redis.call('INCR', KEYS[1])
        if count == 1 then redis.call('EXPIRE', KEYS[1], ARGV[2]) end
        redis.call('SET', KEYS[2], ARGV[4], 'EX', ARGV[3])
        return 1
        """;

    public const string CompleteSend = """
        if redis.call('GET', KEYS[1]) ~= ARGV[1] then return 0 end
        redis.call('DEL', KEYS[2])
        redis.call('HSET', KEYS[2], 'hash', ARGV[2], 'attempts', ARGV[3])
        redis.call('EXPIRE', KEYS[2], ARGV[4])
        redis.call('SET', KEYS[1], ARGV[1], 'EX', ARGV[5])
        return 1
        """;

    public const string ReleaseSend = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    public const string ConsumeCode = """
        local hash = redis.call('HGET', KEYS[1], 'hash')
        if not hash then return -1 end
        if hash ~= ARGV[1] then
            local remaining = redis.call('HINCRBY', KEYS[1], 'attempts', -1)
            if remaining <= 0 then redis.call('DEL', KEYS[1]) end
            return 0
        end
        redis.call('DEL', KEYS[1])
        return 1
        """;
}
