using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Nadosh.Core.Configuration;
using Nadosh.Core.Interfaces;
using Nadosh.Infrastructure.Queue;
using Xunit;

namespace Nadosh.Infrastructure.Tests.Queue;

[Collection(RedisQueueCollection.Name)]
public sealed class RedisJobQueueTests
{
    private readonly RedisQueueFixture _fixture;

    public RedisJobQueueTests(RedisQueueFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task EnqueueDequeueAcknowledge_RemovesProcessingEntryAndLease()
    {
        await _fixture.ResetAsync();
        var database = _fixture.GetDatabaseOrSkip();
        var queue = _fixture.CreateQueue<AckJob>();

        await queue.EnqueueAsync(new AckJob("alpha"));

        var message = await queue.DequeueAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(message);
        Assert.Equal("alpha", message.Payload.Value);
        Assert.Equal(1, message.AttemptCount);
        Assert.False(string.IsNullOrWhiteSpace(message.LeaseToken));
        Assert.False(string.IsNullOrWhiteSpace(message.ProcessingPayload));
        Assert.Equal(1, await database.ListLengthAsync(RedisQueueFixture.ProcessingQueueKey<AckJob>()));
        Assert.True(await database.KeyExistsAsync(RedisQueueFixture.LeaseKey<AckJob>(message.JobId)));

        await queue.AcknowledgeAsync(message);

        Assert.Equal(0, await database.ListLengthAsync(RedisQueueFixture.ProcessingQueueKey<AckJob>()));
        Assert.False(await database.KeyExistsAsync(RedisQueueFixture.LeaseKey<AckJob>(message.JobId)));
    }

    [SkippableFact]
    public async Task RejectWithReenqueue_MakesJobVisibleAgainWithIncrementedAttemptCount()
    {
        await _fixture.ResetAsync();
        var database = _fixture.GetDatabaseOrSkip();
        var queue = _fixture.CreateQueue<RetryJob>();

        await queue.EnqueueAsync(new RetryJob("beta"));
        var firstDelivery = await queue.DequeueAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(firstDelivery);
        Assert.Equal(1, firstDelivery.AttemptCount);

        await queue.RejectAsync(firstDelivery, reenqueue: true);

        Assert.Equal(0, await database.ListLengthAsync(RedisQueueFixture.ProcessingQueueKey<RetryJob>()));
        Assert.False(await database.KeyExistsAsync(RedisQueueFixture.LeaseKey<RetryJob>(firstDelivery.JobId)));

        var secondDelivery = await queue.DequeueAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(secondDelivery);
        Assert.Equal(firstDelivery.JobId, secondDelivery.JobId);
        Assert.Equal(2, secondDelivery.AttemptCount);
        Assert.Equal("beta", secondDelivery.Payload.Value);

        await queue.AcknowledgeAsync(secondDelivery);
    }

    [SkippableFact]
    public async Task RejectWithDelay_KeepsJobInvisibleUntilBackoffExpires()
    {
        await _fixture.ResetAsync();
        var database = _fixture.GetDatabaseOrSkip();
        var queue = _fixture.CreateQueue<BackoffJob>();

        await queue.EnqueueAsync(new BackoffJob("beta-delayed"));
        var firstDelivery = await queue.DequeueAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(firstDelivery);
        Assert.Equal(1, firstDelivery.AttemptCount);

        await queue.RejectAsync(firstDelivery, reenqueue: true, reenqueueDelay: TimeSpan.FromMilliseconds(300));

        Assert.Equal(0, await database.ListLengthAsync(RedisQueueFixture.ProcessingQueueKey<BackoffJob>()));
        Assert.Equal(1, await database.SortedSetLengthAsync(RedisQueueFixture.DelayedQueueKey<BackoffJob>()));
        Assert.Null(await queue.DequeueAsync(TimeSpan.FromSeconds(30)));

        await Task.Delay(350);

        var secondDelivery = await queue.DequeueAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(secondDelivery);
        Assert.Equal(firstDelivery.JobId, secondDelivery.JobId);
        Assert.Equal(2, secondDelivery.AttemptCount);
        Assert.Equal("beta-delayed", secondDelivery.Payload.Value);

        await queue.AcknowledgeAsync(secondDelivery);
    }

    [SkippableFact]
    public async Task DelayedJob_IsInvisibleUntilDueAndThenPromoted()
    {
        await _fixture.ResetAsync();
        var database = _fixture.GetDatabaseOrSkip();
        var queue = _fixture.CreateQueue<DelayedJob>();

        await queue.EnqueueDelayedAsync(new DelayedJob("gamma"), TimeSpan.FromMilliseconds(250));

        var beforeDue = await queue.DequeueAsync(TimeSpan.FromSeconds(30));

        Assert.Null(beforeDue);
        Assert.Equal(1, await database.SortedSetLengthAsync(RedisQueueFixture.DelayedQueueKey<DelayedJob>()));

        await Task.Delay(350);

        var afterDue = await queue.DequeueAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(afterDue);
        Assert.Equal("gamma", afterDue.Payload.Value);
        Assert.Equal(1, afterDue.AttemptCount);
        Assert.Equal(0, await database.SortedSetLengthAsync(RedisQueueFixture.DelayedQueueKey<DelayedJob>()));

        await queue.AcknowledgeAsync(afterDue);
    }

    [SkippableFact]
    public async Task MalformedPayload_IsRemovedFromProcessingAndMovedToDeadLetter()
    {
        await _fixture.ResetAsync();
        var database = _fixture.GetDatabaseOrSkip();
        var queue = _fixture.CreateQueue<MalformedJob>();

        await database.ListLeftPushAsync(RedisQueueFixture.QueueKey<MalformedJob>(), "{ definitely-not-valid-json");

        var result = await queue.DequeueAsync(TimeSpan.FromSeconds(30));

        Assert.Null(result);
        Assert.Equal(0, await database.ListLengthAsync(RedisQueueFixture.ProcessingQueueKey<MalformedJob>()));
        Assert.Equal(1, await database.ListLengthAsync(RedisQueueFixture.DeadLetterQueueKey<MalformedJob>()));

        var deadLetterEntry = await database.ListRightPopAsync(RedisQueueFixture.DeadLetterQueueKey<MalformedJob>());
        using var deadLetterJson = JsonDocument.Parse(deadLetterEntry.ToString());
        Assert.True(deadLetterJson.RootElement.TryGetProperty("rawPayload", out var rawPayload));
        Assert.Contains("definitely-not-valid-json", rawPayload.GetString(), StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task ExpiredLease_IsRecoveredAndStaleConsumerCannotAcknowledge()
    {
        await _fixture.ResetAsync();
        var queue = _fixture.CreateQueue<LeaseRecoveryJob>();

        await queue.EnqueueAsync(new LeaseRecoveryJob("delta"));
        var firstDelivery = await queue.DequeueAsync(TimeSpan.FromMilliseconds(200));

        Assert.NotNull(firstDelivery);
        Assert.Equal(1, firstDelivery.AttemptCount);

        await Task.Delay(350);

        var recoveredDelivery = await queue.DequeueAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(recoveredDelivery);
        Assert.Equal(firstDelivery.JobId, recoveredDelivery.JobId);
        Assert.Equal(2, recoveredDelivery.AttemptCount);

        await Assert.ThrowsAsync<InvalidOperationException>(() => queue.AcknowledgeAsync(firstDelivery));
        await queue.AcknowledgeAsync(recoveredDelivery);
    }

    [SkippableFact]
    public async Task RenewLease_PreventsPrematureRecovery()
    {
        await _fixture.ResetAsync();
        var queue = _fixture.CreateQueue<LeaseRenewalJob>();

        await queue.EnqueueAsync(new LeaseRenewalJob("epsilon"));
        var delivery = await queue.DequeueAsync(TimeSpan.FromMilliseconds(200));

        Assert.NotNull(delivery);
        Assert.Equal(1, delivery.AttemptCount);

        await Task.Delay(125);
        await queue.RenewLeaseAsync(delivery, TimeSpan.FromMilliseconds(800));
        await Task.Delay(175);

        Assert.Null(await queue.DequeueAsync(TimeSpan.FromSeconds(30)));

        await queue.AcknowledgeAsync(delivery);
    }

    [SkippableFact]
    public async Task DuplicateIdempotencyKey_IsSuppressedAcrossImmediateAndDelayedEnqueue()
    {
        await _fixture.ResetAsync();
        var database = _fixture.GetDatabaseOrSkip();
        var queue = _fixture.CreateQueue<IdempotentJob>();

        await queue.EnqueueAsync(new IdempotentJob("first"), idempotencyKey: "dup-key");
        await queue.EnqueueAsync(new IdempotentJob("second"), idempotencyKey: "dup-key");

        Assert.Equal(1, await database.ListLengthAsync(RedisQueueFixture.QueueKey<IdempotentJob>()));

        var firstDelivery = await queue.DequeueAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(firstDelivery);
        Assert.Equal("first", firstDelivery.Payload.Value);
        Assert.Equal("dup-key", firstDelivery.IdempotencyKey);

        await queue.AcknowledgeAsync(firstDelivery);
        await _fixture.ResetAsync();

        var crossPathQueue = _fixture.CreateQueue<CrossPathIdempotentJob>();

        await crossPathQueue.EnqueueDelayedAsync(new CrossPathIdempotentJob("delayed"), TimeSpan.FromMilliseconds(250), idempotencyKey: "shared-key");
        await crossPathQueue.EnqueueAsync(new CrossPathIdempotentJob("immediate"), idempotencyKey: "shared-key");

        Assert.Equal(0, await database.ListLengthAsync(RedisQueueFixture.QueueKey<CrossPathIdempotentJob>()));
        Assert.Equal(1, await database.SortedSetLengthAsync(RedisQueueFixture.DelayedQueueKey<CrossPathIdempotentJob>()));

        await Task.Delay(350);

        var recoveredDelayed = await crossPathQueue.DequeueAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(recoveredDelayed);
        Assert.Equal("delayed", recoveredDelayed.Payload.Value);
        Assert.Equal("shared-key", recoveredDelayed.IdempotencyKey);

        await crossPathQueue.AcknowledgeAsync(recoveredDelayed);
    }

    [SkippableFact]
    public async Task ConfiguredIdempotencyWindowExpiry_AllowsReenqueue()
    {
        await _fixture.ResetAsync();
        var database = _fixture.GetDatabaseOrSkip();
        var queue = _fixture.CreateQueue<ShortWindowJob>(new ResolvedQueuePolicy
        {
            QueueName = "shortwindowjob",
            VisibilityTimeoutSeconds = 30,
            MaxAttempts = 3,
            RetryBaseDelaySeconds = 1,
            MaxRetryDelaySeconds = 5,
            IdempotencyWindowSeconds = 1
        });

        await queue.EnqueueAsync(new ShortWindowJob("first"), idempotencyKey: "expiring-key");
        await queue.EnqueueAsync(new ShortWindowJob("second"), idempotencyKey: "expiring-key");

        Assert.Equal(1, await database.ListLengthAsync(RedisQueueFixture.QueueKey<ShortWindowJob>()));

        await Task.Delay(1100);

        await queue.EnqueueAsync(new ShortWindowJob("third"), idempotencyKey: "expiring-key");

        Assert.Equal(2, await database.ListLengthAsync(RedisQueueFixture.QueueKey<ShortWindowJob>()));
    }

    [SkippableFact]
    public async Task HigherPriorityJobs_DequeueBeforeNormalAndLowPriority()
    {
        await _fixture.ResetAsync();
        var queue = _fixture.CreateQueue<PriorityJob>();

        await queue.EnqueueAsync(new PriorityJob("low"), priority: -1);
        await queue.EnqueueAsync(new PriorityJob("normal"));
        await queue.EnqueueAsync(new PriorityJob("high"), priority: 1);

        var first = await queue.DequeueAsync(TimeSpan.FromSeconds(30));
        var second = await queue.DequeueAsync(TimeSpan.FromSeconds(30));
        var third = await queue.DequeueAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(third);
        Assert.Equal("high", first.Payload.Value);
        Assert.Equal(1, first.Priority);
        Assert.Equal("normal", second.Payload.Value);
        Assert.Equal(0, second.Priority);
        Assert.Equal("low", third.Payload.Value);
        Assert.Equal(-1, third.Priority);

        await queue.AcknowledgeAsync(first);
        await queue.AcknowledgeAsync(second);
        await queue.AcknowledgeAsync(third);
    }

    [SkippableFact]
    public async Task SubscribedShard_OnlyDequeuesMatchingShardJobs()
    {
        await _fixture.ResetAsync();
        var database = _fixture.GetDatabaseOrSkip();
        var queue = _fixture.CreateQueue<ShardedJob>(new ResolvedQueuePolicy
        {
            QueueName = "shardedjob",
            ShardCount = 2,
            SubscribedShards = [1],
            VisibilityTimeoutSeconds = 30,
            MaxAttempts = 3,
            RetryBaseDelaySeconds = 1,
            MaxRetryDelaySeconds = 5,
            IdempotencyWindowSeconds = 3600
        });

        var shard0Key = FindKeyForShard(2, 0);
        var shard1Key = FindKeyForShard(2, 1);

        await queue.EnqueueAsync(new ShardedJob("shard-zero"), enqueueOptions: new JobEnqueueOptions { ShardKey = shard0Key }, idempotencyKey: null, priority: 0);
        await queue.EnqueueAsync(new ShardedJob("shard-one"), enqueueOptions: new JobEnqueueOptions { ShardKey = shard1Key }, idempotencyKey: null, priority: 0);

        Assert.Equal(1, await database.ListLengthAsync(RedisQueueFixture.QueueKey<ShardedJob>(0)));
        Assert.Equal(1, await database.ListLengthAsync(RedisQueueFixture.QueueKey<ShardedJob>(1)));

        var delivery = await queue.DequeueAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(delivery);
        Assert.Equal("shard-one", delivery.Payload.Value);
        Assert.Equal(1, delivery.ShardId);
        Assert.Equal(1, await database.ListLengthAsync(RedisQueueFixture.QueueKey<ShardedJob>(0)));
        Assert.Equal(1, await database.ListLengthAsync(RedisQueueFixture.ProcessingQueueKey<ShardedJob>(1)));

        await queue.AcknowledgeAsync(delivery);
    }

    [SkippableFact]
    public async Task DelayedJob_PromotesWithinAssignedShard()
    {
        await _fixture.ResetAsync();
        var database = _fixture.GetDatabaseOrSkip();
        var queue = _fixture.CreateQueue<ShardedDelayedJob>(new ResolvedQueuePolicy
        {
            QueueName = "shardeddelayedjob",
            ShardCount = 2,
            SubscribedShards = [1],
            VisibilityTimeoutSeconds = 30,
            MaxAttempts = 3,
            RetryBaseDelaySeconds = 1,
            MaxRetryDelaySeconds = 5,
            IdempotencyWindowSeconds = 3600
        });

        var shard1Key = FindKeyForShard(2, 1);

        await queue.EnqueueDelayedAsync(
            new ShardedDelayedJob("later"),
            TimeSpan.FromMilliseconds(250),
            idempotencyKey: null,
            priority: 0,
            enqueueOptions: new JobEnqueueOptions { ShardKey = shard1Key });

        Assert.Equal(1, await database.SortedSetLengthAsync(RedisQueueFixture.DelayedQueueKey<ShardedDelayedJob>(1)));
        Assert.Equal(0, await database.SortedSetLengthAsync(RedisQueueFixture.DelayedQueueKey<ShardedDelayedJob>(0)));

        Assert.Null(await queue.DequeueAsync(TimeSpan.FromSeconds(30)));

        await Task.Delay(350);

        var delivery = await queue.DequeueAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(delivery);
        Assert.Equal("later", delivery.Payload.Value);
        Assert.Equal(1, delivery.ShardId);
        Assert.Equal(0, await database.SortedSetLengthAsync(RedisQueueFixture.DelayedQueueKey<ShardedDelayedJob>(1)));
        Assert.Equal(1, await database.ListLengthAsync(RedisQueueFixture.ProcessingQueueKey<ShardedDelayedJob>(1)));

        await queue.AcknowledgeAsync(delivery);
    }

    private static string FindKeyForShard(int shardCount, int targetShard)
    {
        for (var i = 0; i < 10_000; i++)
        {
            var candidate = $"candidate-{targetShard}-{i}";
            if (ComputeShard(candidate, shardCount) == targetShard)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"Unable to find shard key for shard {targetShard} out of {shardCount} shards.");
    }

    private static int ComputeShard(string value, int shardCount)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var hash = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, sizeof(uint)));
        return (int)(hash % (uint)shardCount);
    }

    private sealed record AckJob(string Value);
    private sealed record RetryJob(string Value);
    private sealed record BackoffJob(string Value);
    private sealed record DelayedJob(string Value);
    private sealed record MalformedJob(string Value);
    private sealed record LeaseRecoveryJob(string Value);
    private sealed record LeaseRenewalJob(string Value);
    private sealed record IdempotentJob(string Value);
    private sealed record CrossPathIdempotentJob(string Value);
    private sealed record ShortWindowJob(string Value);
    private sealed record PriorityJob(string Value);
    private sealed record ShardedJob(string Value);
    private sealed record ShardedDelayedJob(string Value);
}
