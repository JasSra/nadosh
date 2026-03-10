using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nadosh.Core.Interfaces;
using StackExchange.Redis;

namespace Nadosh.Infrastructure.Queue;

public class RedisJobQueue<T> : IJobQueue<T>
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly string _queueName;

    public RedisJobQueue(IConnectionMultiplexer redis)
    {
        _redis = redis;
        _db = _redis.GetDatabase();
        _queueName = $"queue:{typeof(T).Name.ToLowerInvariant()}";
    }

    public async Task EnqueueAsync(T payload, string? idempotencyKey = null, int priority = 0, CancellationToken cancellationToken = default)
    {
        var message = new JobQueueMessage<T> { Payload = payload };
        var json = JsonSerializer.Serialize(message);
        
        // Push to Redis List (acting as a queue). For priority, you'd use multiple lists or sorted sets.
        await _db.ListLeftPushAsync(_queueName, json);
    }

    public async Task EnqueueDelayedAsync(T payload, TimeSpan delay, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var message = new JobQueueMessage<T> { Payload = payload };
        var json = JsonSerializer.Serialize(message);
        
        // Store in a Sorted Set with score as the unlock time
        var executeAt = DateTimeOffset.UtcNow.Add(delay).ToUnixTimeMilliseconds();
        await _db.SortedSetAddAsync($"{_queueName}:delayed", json, executeAt);
    }

    public async Task<JobQueueMessage<T>?> DequeueAsync(TimeSpan visibilityTimeout, CancellationToken cancellationToken = default)
    {
        // Atomically pop and push to a processing list (RPOPLPUSH / LMOVE equivalent)
        var result = await _db.ListRightPopLeftPushAsync(_queueName, $"{_queueName}:processing");
        
        if (result.IsNull)
            return null;

        var message = JsonSerializer.Deserialize<JobQueueMessage<T>>(result.ToString()!);
        if (message == null) return null;

        message.AttemptCount++;
        message.LeaseToken = Guid.NewGuid().ToString();

        // Track lease timeout
        await _db.StringSetAsync($"lease:{message.JobId}", message.LeaseToken, visibilityTimeout);

        return message;
    }

    public async Task AcknowledgeAsync(JobQueueMessage<T> message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message);
        await _db.ListRemoveAsync($"{_queueName}:processing", json, 1);
        await _db.KeyDeleteAsync($"lease:{message.JobId}");
    }

    public async Task RejectAsync(JobQueueMessage<T> message, bool reenqueue = true, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message);
        await _db.ListRemoveAsync($"{_queueName}:processing", json, 1);
        await _db.KeyDeleteAsync($"lease:{message.JobId}");

        if (reenqueue)
        {
            await _db.ListLeftPushAsync(_queueName, json);
        }
    }

    public async Task DeadLetterAsync(JobQueueMessage<T> message, string errorReason, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message);
        await _db.ListRemoveAsync($"{_queueName}:processing", json, 1);
        await _db.KeyDeleteAsync($"lease:{message.JobId}");
        
        await _db.ListLeftPushAsync($"{_queueName}:dlq", json);
    }

    public async Task RenewLeaseAsync(JobQueueMessage<T> message, TimeSpan additionalTimeout, CancellationToken cancellationToken = default)
    {
        var currentToken = await _db.StringGetAsync($"lease:{message.JobId}");
        if (currentToken == message.LeaseToken)
        {
            await _db.KeyExpireAsync($"lease:{message.JobId}", additionalTimeout);
        }
    }
}
