using Nadosh.Core.Interfaces;
using StackExchange.Redis;

namespace Nadosh.Infrastructure.Scanning;

/// <summary>
/// Redis-based distributed token-bucket rate limiter.
/// Enforces per-/24 CIDR and global packets-per-second limits
/// shared across all worker instances to ensure responsible scanning.
/// </summary>
public class RedisScanRateLimiter : IScanRateLimiter
{
    private readonly IDatabase _db;
    private readonly int _maxPerSecondPerSubnet;
    private readonly int _maxGlobalPerSecond;

    // Lua script for atomic token-bucket check-and-decrement
    private const string TokenBucketScript = @"
        local key = KEYS[1]
        local max_tokens = tonumber(ARGV[1])
        local refill_rate = tonumber(ARGV[2])
        local now = tonumber(ARGV[3])

        local bucket = redis.call('HMGET', key, 'tokens', 'last_refill')
        local tokens = tonumber(bucket[1]) or max_tokens
        local last_refill = tonumber(bucket[2]) or now

        -- Refill tokens based on elapsed time
        local elapsed = now - last_refill
        tokens = math.min(max_tokens, tokens + (elapsed * refill_rate))

        if tokens >= 1 then
            tokens = tokens - 1
            redis.call('HMSET', key, 'tokens', tokens, 'last_refill', now)
            redis.call('EXPIRE', key, 10)
            return 1
        else
            redis.call('HMSET', key, 'tokens', tokens, 'last_refill', now)
            redis.call('EXPIRE', key, 10)
            return 0
        end
    ";

    public RedisScanRateLimiter(IConnectionMultiplexer redis, int maxPerSecondPerSubnet = 50, int maxGlobalPerSecond = 5000)
    {
        _db = redis.GetDatabase();
        _maxPerSecondPerSubnet = maxPerSecondPerSubnet;
        _maxGlobalPerSecond = maxGlobalPerSecond;
    }

    public async Task<bool> TryAcquireAsync(string targetIp, CancellationToken ct = default)
    {
        var cidr24 = GetCidr24(targetIp);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        // Check subnet-level limit
        var subnetAllowed = (int)await _db.ScriptEvaluateAsync(
            TokenBucketScript,
            new RedisKey[] { $"ratelimit:subnet:{cidr24}" },
            new RedisValue[] { _maxPerSecondPerSubnet, _maxPerSecondPerSubnet, now });

        if (subnetAllowed == 0) return false;

        // Check global limit
        var globalAllowed = (int)await _db.ScriptEvaluateAsync(
            TokenBucketScript,
            new RedisKey[] { "ratelimit:global" },
            new RedisValue[] { _maxGlobalPerSecond, _maxGlobalPerSecond, now });

        return globalAllowed == 1;
    }

    public async Task<double> GetUtilisationAsync(string cidr24, CancellationToken ct = default)
    {
        var bucket = await _db.HashGetAllAsync($"ratelimit:subnet:{cidr24}");
        if (bucket.Length == 0) return 0.0;

        var tokensEntry = bucket.FirstOrDefault(e => e.Name == "tokens");
        var tokens = tokensEntry.Value.IsNull ? _maxPerSecondPerSubnet : (double)tokensEntry.Value;
        return 1.0 - (tokens / _maxPerSecondPerSubnet);
    }

    private static string GetCidr24(string ip)
    {
        var parts = ip.Split('.');
        if (parts.Length == 4)
            return $"{parts[0]}.{parts[1]}.{parts[2]}.0/24";
        return ip; // IPv6 fallback
    }
}
