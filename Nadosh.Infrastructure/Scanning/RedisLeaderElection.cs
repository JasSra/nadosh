using Nadosh.Core.Interfaces;
using StackExchange.Redis;

namespace Nadosh.Infrastructure.Scanning;

/// <summary>
/// Redis-based distributed leader election using SETNX with TTL.
/// Ensures only one scheduler instance runs across the entire cluster.
/// </summary>
public class RedisLeaderElection : ILeaderElection
{
    private readonly IDatabase _db;

    public RedisLeaderElection(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    public async Task<bool> TryAcquireLeadershipAsync(string resource, string instanceId, TimeSpan ttl, CancellationToken ct = default)
    {
        var key = $"leader:{resource}";
        return await _db.StringSetAsync(key, instanceId, ttl, When.NotExists);
    }

    public async Task<bool> RenewLeadershipAsync(string resource, string instanceId, TimeSpan ttl, CancellationToken ct = default)
    {
        var key = $"leader:{resource}";
        var currentLeader = await _db.StringGetAsync(key);

        if (currentLeader == instanceId)
        {
            return await _db.KeyExpireAsync(key, ttl);
        }

        return false;
    }

    public async Task ReleaseLeadershipAsync(string resource, string instanceId, CancellationToken ct = default)
    {
        var key = $"leader:{resource}";
        var currentLeader = await _db.StringGetAsync(key);

        if (currentLeader == instanceId)
        {
            await _db.KeyDeleteAsync(key);
        }
    }
}
