using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nadosh.Core.Configuration;
using Nadosh.Core.Interfaces;
using StackExchange.Redis;

namespace Nadosh.Infrastructure.Queue;

public class RedisJobQueue<T> : IJobQueue<T>
{
    private const int RecoveryBatchSize = 100;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisJobQueue<T>> _logger;
    private readonly string _queueName;
    private readonly ResolvedQueuePolicy _policy;

    public RedisJobQueue(IConnectionMultiplexer redis, IQueuePolicyProvider queuePolicyProvider, ILogger<RedisJobQueue<T>> logger)
    {
        _redis = redis;
        _db = _redis.GetDatabase();
        _logger = logger;
        _queueName = $"queue:{typeof(T).Name.ToLowerInvariant()}";
        _policy = queuePolicyProvider.GetPolicy(typeof(T).Name);
    }

    public Task EnqueueAsync(T payload, string? idempotencyKey = null, int priority = 0, CancellationToken cancellationToken = default)
        => EnqueueAsync(payload, idempotencyKey, priority, enqueueOptions: null, cancellationToken);

    public async Task EnqueueAsync(
        T payload,
        string? idempotencyKey,
        int priority,
        JobEnqueueOptions? enqueueOptions,
        CancellationToken cancellationToken = default)
    {
        var message = CreateMessage(payload, idempotencyKey, priority, enqueueOptions?.ShardKey);
        var json = JsonSerializer.Serialize(message, SerializerOptions);

        if (await TryEnqueueWithIdempotencyAsync(message, json, delayedBy: null, cancellationToken))
        {
            return;
        }

        await _db.ListLeftPushAsync(GetReadyQueueName(message.Priority, message.ShardId), json);
    }

    public Task EnqueueDelayedAsync(T payload, TimeSpan delay, string? idempotencyKey = null, int priority = 0, CancellationToken cancellationToken = default)
        => EnqueueDelayedAsync(payload, delay, idempotencyKey, priority, enqueueOptions: null, cancellationToken);

    public async Task EnqueueDelayedAsync(
        T payload,
        TimeSpan delay,
        string? idempotencyKey,
        int priority,
        JobEnqueueOptions? enqueueOptions,
        CancellationToken cancellationToken = default)
    {
        var message = CreateMessage(payload, idempotencyKey, priority, enqueueOptions?.ShardKey);
        var json = JsonSerializer.Serialize(message, SerializerOptions);

        if (await TryEnqueueWithIdempotencyAsync(message, json, delay, cancellationToken))
        {
            return;
        }

        var executeAt = DateTimeOffset.UtcNow.Add(delay).ToUnixTimeMilliseconds();
        await _db.SortedSetAddAsync(GetDelayedQueueName(message.ShardId), json, executeAt);
    }

    public async Task<JobQueueMessage<T>?> DequeueAsync(TimeSpan visibilityTimeout, CancellationToken cancellationToken = default)
    {
        await PromoteDueDelayedJobsAsync(cancellationToken);
        await RecoverExpiredLeasesAsync(cancellationToken);

        var dequeuedPayload = await DequeueReadyPayloadAsync();
        if (dequeuedPayload == null)
        {
            return null;
        }

        var rawPayload = dequeuedPayload.RawPayload.ToString()!;
        JobQueueMessage<T>? message;

        try
        {
            message = JsonSerializer.Deserialize<JobQueueMessage<T>>(rawPayload, SerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize dequeued job payload for queue {QueueName}; moving raw payload to dead-letter.", _queueName);
            await RemoveProcessingPayloadIfPresentAsync(rawPayload, dequeuedPayload.ProcessingQueueName);
            await PushMalformedPayloadToDeadLetterAsync(rawPayload, ex.Message, dequeuedPayload.ProcessingQueueName, dequeuedPayload.ShardId);
            return null;
        }

        if (message == null)
        {
            _logger.LogError("Dequeued payload for queue {QueueName} deserialized to null; moving raw payload to dead-letter.", _queueName);
            await RemoveProcessingPayloadIfPresentAsync(rawPayload, dequeuedPayload.ProcessingQueueName);
            await PushMalformedPayloadToDeadLetterAsync(rawPayload, "Deserialized message was null.", dequeuedPayload.ProcessingQueueName, dequeuedPayload.ShardId);
            return null;
        }

        message.ShardId = dequeuedPayload.ShardId;
        message.AttemptCount++;
        message.LeaseToken = Guid.NewGuid().ToString();
        message.ProcessingPayload = rawPayload;

        await _db.StringSetAsync(GetLeaseKey(message.JobId), message.LeaseToken, visibilityTimeout);
        return message;
    }

    public async Task AcknowledgeAsync(JobQueueMessage<T> message, CancellationToken cancellationToken = default)
    {
        await EnsureLeaseOwnershipAsync(message);
        await RemoveFromProcessingOrThrowAsync(message);
        await _db.KeyDeleteAsync(GetLeaseKey(message.JobId));
    }

    public async Task RejectAsync(JobQueueMessage<T> message, bool reenqueue = true, TimeSpan? reenqueueDelay = null, CancellationToken cancellationToken = default)
    {
        await EnsureLeaseOwnershipAsync(message);
        await RemoveFromProcessingOrThrowAsync(message);
        await _db.KeyDeleteAsync(GetLeaseKey(message.JobId));

        if (!reenqueue)
        {
            return;
        }

        message.LeaseToken = null;
        message.ProcessingPayload = null;
        var json = JsonSerializer.Serialize(CloneForPersistence(message), SerializerOptions);

        if (reenqueueDelay.HasValue && reenqueueDelay.Value > TimeSpan.Zero)
        {
            var executeAt = DateTimeOffset.UtcNow.Add(reenqueueDelay.Value).ToUnixTimeMilliseconds();
            await _db.SortedSetAddAsync(GetDelayedQueueName(message.ShardId), json, executeAt);
            return;
        }

        await _db.ListLeftPushAsync(GetReadyQueueName(message.Priority, message.ShardId), json);
    }

    public async Task DeadLetterAsync(JobQueueMessage<T> message, string errorReason, CancellationToken cancellationToken = default)
    {
        await EnsureLeaseOwnershipAsync(message);
        await RemoveFromProcessingOrThrowAsync(message);
        await _db.KeyDeleteAsync(GetLeaseKey(message.JobId));

        message.LeaseToken = null;
        message.ProcessingPayload = null;
        var deadLetterEntry = JsonSerializer.Serialize(new DeadLetterQueueEntry<JobQueueMessage<T>>
        {
            SourceQueue = GetProcessingQueueName(message.ShardId),
            ErrorReason = errorReason,
            DeadLetteredAt = DateTime.UtcNow,
            Payload = CloneForPersistence(message)
        }, SerializerOptions);

        await _db.ListLeftPushAsync(GetDeadLetterQueueName(message.ShardId), deadLetterEntry);
    }

    public async Task RenewLeaseAsync(JobQueueMessage<T> message, TimeSpan additionalTimeout, CancellationToken cancellationToken = default)
    {
        var currentToken = await _db.StringGetAsync(GetLeaseKey(message.JobId));
        if (currentToken == message.LeaseToken)
        {
            await _db.KeyExpireAsync(GetLeaseKey(message.JobId), additionalTimeout);
        }
    }

    private JobQueueMessage<T> CreateMessage(T payload, string? idempotencyKey, int priority, string? shardKey)
        => new()
        {
            Payload = payload,
            ShardId = ResolveShardId(shardKey),
            Priority = NormalizePriority(priority),
            IdempotencyKey = NormalizeIdempotencyKey(idempotencyKey)
        };

    private async Task PromoteDueDelayedJobsAsync(CancellationToken cancellationToken)
    {
        foreach (var shardId in GetSubscribedShards())
        {
            var delayedQueueName = GetDelayedQueueName(shardId);
            var dueMessages = await _db.SortedSetRangeByScoreAsync(
                delayedQueueName,
                stop: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                take: 100);

            foreach (var dueMessage in dueMessages)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (!await _db.SortedSetRemoveAsync(delayedQueueName, dueMessage))
                {
                    continue;
                }

                var rawPayload = dueMessage.ToString()!;
                if (TryDeserializeMessage(rawPayload, out var message, out var errorReason))
                {
                    message!.ShardId = shardId;
                    var promotedPayload = JsonSerializer.Serialize(CloneForPersistence(message), SerializerOptions);
                    await _db.ListLeftPushAsync(GetReadyQueueName(message.Priority, shardId), promotedPayload);
                    continue;
                }

                _logger.LogError(
                    "Failed to deserialize delayed payload for queue {QueueName}; moving raw payload to dead-letter. {Reason}",
                    _queueName,
                    errorReason);
                await PushMalformedPayloadToDeadLetterAsync(rawPayload, errorReason, delayedQueueName, shardId);
            }
        }
    }

    private async Task RecoverExpiredLeasesAsync(CancellationToken cancellationToken)
    {
        foreach (var shardId in GetSubscribedShards())
        {
            var processingQueueName = GetProcessingQueueName(shardId);
            var processingEntries = await _db.ListRangeAsync(processingQueueName, 0, RecoveryBatchSize - 1);

            foreach (var processingEntry in processingEntries)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (processingEntry.IsNull)
                {
                    continue;
                }

                var rawPayload = processingEntry.ToString()!;
                JobQueueMessage<T>? message;

                try
                {
                    message = JsonSerializer.Deserialize<JobQueueMessage<T>>(rawPayload, SerializerOptions);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize processing payload for queue {QueueName}; removing it from processing and dead-lettering the raw payload.", _queueName);
                    await RemoveProcessingPayloadIfPresentAsync(rawPayload, processingQueueName);
                    await PushMalformedPayloadToDeadLetterAsync(rawPayload, $"Lease recovery deserialization failed: {ex.Message}", processingQueueName, shardId);
                    continue;
                }

                if (message == null)
                {
                    _logger.LogError("Processing payload for queue {QueueName} deserialized to null during lease recovery; removing it from processing and dead-lettering the raw payload.", _queueName);
                    await RemoveProcessingPayloadIfPresentAsync(rawPayload, processingQueueName);
                    await PushMalformedPayloadToDeadLetterAsync(rawPayload, "Lease recovery deserialized message was null.", processingQueueName, shardId);
                    continue;
                }

                if (await _db.KeyExistsAsync(GetLeaseKey(message.JobId)))
                {
                    continue;
                }

                message.ShardId = shardId;
                message.AttemptCount++;
                message.LeaseToken = null;
                message.ProcessingPayload = null;

                var recoveredPayload = JsonSerializer.Serialize(CloneForPersistence(message), SerializerOptions);
                var removed = await _db.ListRemoveAsync(processingQueueName, rawPayload, 1);
                if (removed == 0)
                {
                    continue;
                }

                await _db.ListLeftPushAsync(GetReadyQueueName(message.Priority, shardId), recoveredPayload);
                _logger.LogWarning(
                    "Recovered expired lease for job {JobId} on queue {QueueName} shard {ShardId}; job was made visible again.",
                    message.JobId,
                    _queueName,
                    shardId);
            }
        }
    }

    private async Task<DequeuedPayload?> DequeueReadyPayloadAsync()
    {
        foreach (var priority in GetDequeuePriorityOrder())
        {
            foreach (var shardId in GetSubscribedShards())
            {
                var processingQueueName = GetProcessingQueueName(shardId);
                var readyQueueName = GetReadyQueueName(priority, shardId);
                var result = await _db.ListRightPopLeftPushAsync(readyQueueName, processingQueueName);
                if (!result.IsNull)
                {
                    return new DequeuedPayload(shardId, processingQueueName, result);
                }
            }
        }

        return null;
    }

    private async Task RemoveFromProcessingOrThrowAsync(JobQueueMessage<T> message)
    {
        var processingQueueName = GetProcessingQueueName(message.ShardId);
        var processingPayload = message.ProcessingPayload ?? JsonSerializer.Serialize(CloneForPersistence(message), SerializerOptions);
        var removed = await _db.ListRemoveAsync(processingQueueName, processingPayload, 1);
        if (removed == 0)
        {
            throw new InvalidOperationException(
                $"Failed to remove processing entry for job {message.JobId} from queue {processingQueueName}.");
        }
    }

    private async Task EnsureLeaseOwnershipAsync(JobQueueMessage<T> message)
    {
        if (string.IsNullOrWhiteSpace(message.LeaseToken))
        {
            throw new InvalidOperationException(
                $"Cannot operate on job {message.JobId} because it does not have an active lease token.");
        }

        var currentToken = await _db.StringGetAsync(GetLeaseKey(message.JobId));
        if (currentToken.IsNullOrEmpty || currentToken != message.LeaseToken)
        {
            throw new InvalidOperationException(
                $"Lease ownership for job {message.JobId} on queue {_queueName} is no longer valid.");
        }
    }

    private async Task<bool> TryEnqueueWithIdempotencyAsync(
        JobQueueMessage<T> message,
        string json,
        TimeSpan? delayedBy,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.IdempotencyKey))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var idempotencyKey = GetIdempotencyRedisKey(message.IdempotencyKey);
        var transaction = _db.CreateTransaction();
        transaction.AddCondition(Condition.KeyNotExists(idempotencyKey));
        _ = transaction.StringSetAsync(idempotencyKey, message.JobId, _policy.IdempotencyWindow);

        if (delayedBy.HasValue)
        {
            var executeAt = DateTimeOffset.UtcNow.Add(delayedBy.Value).ToUnixTimeMilliseconds();
            _ = transaction.SortedSetAddAsync(GetDelayedQueueName(message.ShardId), json, executeAt);
        }
        else
        {
            _ = transaction.ListLeftPushAsync(GetReadyQueueName(message.Priority, message.ShardId), json);
        }

        var accepted = await transaction.ExecuteAsync();
        if (!accepted)
        {
            _logger.LogInformation(
                "Skipping duplicate enqueue for queue {QueueName} because idempotency key {IdempotencyKey} is already active.",
                _queueName,
                message.IdempotencyKey);
        }

        return true;
    }

    private int ResolveShardId(string? shardKey)
    {
        if (_policy.ShardCount <= 1 || string.IsNullOrWhiteSpace(shardKey))
        {
            return 0;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(shardKey.Trim()));
        var hash = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, sizeof(uint)));
        return (int)(hash % (uint)_policy.ShardCount);
    }

    private static string? NormalizeIdempotencyKey(string? idempotencyKey)
        => string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();

    private static int NormalizePriority(int priority)
        => priority switch
        {
            > 0 => 1,
            < 0 => -1,
            _ => 0
        };

    private bool TryDeserializeMessage(string rawPayload, out JobQueueMessage<T>? message, out string errorReason)
    {
        try
        {
            message = JsonSerializer.Deserialize<JobQueueMessage<T>>(rawPayload, SerializerOptions);
        }
        catch (Exception ex)
        {
            message = null;
            errorReason = ex.Message;
            return false;
        }

        if (message == null)
        {
            errorReason = "Deserialized message was null.";
            return false;
        }

        errorReason = string.Empty;
        return true;
    }

    private string GetReadyQueueName(int priority, int shardId)
    {
        var shardBaseName = GetShardQueueBaseName(shardId);
        return NormalizePriority(priority) switch
        {
            > 0 => $"{shardBaseName}:high",
            < 0 => $"{shardBaseName}:low",
            _ => shardBaseName
        };
    }

    private string GetProcessingQueueName(int shardId)
        => $"{GetShardQueueBaseName(shardId)}:processing";

    private string GetDelayedQueueName(int shardId)
        => $"{GetShardQueueBaseName(shardId)}:delayed";

    private string GetDeadLetterQueueName(int shardId)
        => $"{GetShardQueueBaseName(shardId)}:dlq";

    private string GetShardQueueBaseName(int shardId)
    {
        if (_policy.ShardCount <= 1)
        {
            return _queueName;
        }

        return $"{_queueName}:shard:{NormalizeShardId(shardId)}";
    }

    private int NormalizeShardId(int shardId)
    {
        if (_policy.ShardCount <= 1)
        {
            return 0;
        }

        return ((shardId % _policy.ShardCount) + _policy.ShardCount) % _policy.ShardCount;
    }

    private IReadOnlyList<int> GetSubscribedShards()
    {
        if (_policy.SubscribedShards.Count > 0)
        {
            return _policy.SubscribedShards;
        }

        if (_policy.ShardCount <= 1)
        {
            return [0];
        }

        return Enumerable.Range(0, _policy.ShardCount).ToArray();
    }

    private static int[] GetDequeuePriorityOrder()
        => [1, 0, -1];

    private string GetLeaseKey(string jobId)
        => $"{_queueName}:lease:{jobId}";

    private string GetIdempotencyRedisKey(string idempotencyKey)
        => $"{_queueName}:idempotency:{idempotencyKey}";

    private async Task RemoveProcessingPayloadIfPresentAsync(string rawPayload, string processingQueueName)
    {
        var removed = await _db.ListRemoveAsync(processingQueueName, rawPayload, 1);
        if (removed == 0)
        {
            _logger.LogWarning("Malformed payload could not be removed from processing queue {ProcessingQueueName}; it may have already been removed.", processingQueueName);
        }
    }

    private async Task PushMalformedPayloadToDeadLetterAsync(string rawPayload, string errorReason, string sourceQueueName, int shardId)
    {
        var deadLetterEntry = JsonSerializer.Serialize(new MalformedDeadLetterQueueEntry
        {
            SourceQueue = sourceQueueName,
            ErrorReason = errorReason,
            DeadLetteredAt = DateTime.UtcNow,
            RawPayload = rawPayload,
            MessageType = typeof(T).FullName ?? typeof(T).Name
        }, SerializerOptions);

        await _db.ListLeftPushAsync(GetDeadLetterQueueName(shardId), deadLetterEntry);
    }

    private static JobQueueMessage<T> CloneForPersistence(JobQueueMessage<T> message)
        => new()
        {
            JobId = message.JobId,
            Payload = message.Payload,
            ShardId = message.ShardId,
            Priority = message.Priority,
            AttemptCount = message.AttemptCount,
            EnqueuedAt = message.EnqueuedAt,
            IdempotencyKey = message.IdempotencyKey,
            LeaseToken = null
        };

    private sealed record DequeuedPayload(int ShardId, string ProcessingQueueName, RedisValue RawPayload);

    private sealed class DeadLetterQueueEntry<TPayload>
    {
        public string SourceQueue { get; init; } = string.Empty;
        public string ErrorReason { get; init; } = string.Empty;
        public DateTime DeadLetteredAt { get; init; }
        public TPayload Payload { get; init; } = default!;
    }

    private sealed class MalformedDeadLetterQueueEntry
    {
        public string SourceQueue { get; init; } = string.Empty;
        public string ErrorReason { get; init; } = string.Empty;
        public DateTime DeadLetteredAt { get; init; }
        public string RawPayload { get; init; } = string.Empty;
        public string MessageType { get; init; } = string.Empty;
    }
}
