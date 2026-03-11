using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Nadosh.Core.Configuration;
using Nadosh.Core.Interfaces;
using Nadosh.Infrastructure.Queue;
using StackExchange.Redis;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Nadosh.Infrastructure.Tests.Queue;

[CollectionDefinition(Name)]
public sealed class RedisQueueCollection : ICollectionFixture<RedisQueueFixture>
{
    public const string Name = "Redis queue integration";
}

public sealed class RedisQueueFixture : IAsyncLifetime
{
    private const string RedisConnectionString = "localhost:6389,abortConnect=false,connectTimeout=2000,asyncTimeout=2000,syncTimeout=2000,connectRetry=1";

    public IConnectionMultiplexer? Connection { get; private set; }
    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            Connection = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
            await ResetAsync();
        }
        catch (Exception ex)
        {
            SkipReason = $"Redis integration tests require a reachable Redis instance on localhost:6389. {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (Connection is not null)
        {
            await Connection.CloseAsync();
            await Connection.DisposeAsync();
        }
    }

    public async Task ResetAsync()
    {
        var database = GetDatabaseOrSkip();
        await database.ExecuteAsync("FLUSHDB");
    }

    public IDatabase GetDatabaseOrSkip()
    {
        Skip.If(Connection is null, SkipReason ?? "Redis integration fixture could not connect to localhost:6389.");

        return Connection!.GetDatabase();
    }

    public RedisJobQueue<T> CreateQueue<T>(ResolvedQueuePolicy? policy = null)
    {
        Skip.If(Connection is null, SkipReason ?? "Redis integration fixture could not connect to localhost:6389.");

        return new RedisJobQueue<T>(
            Connection!,
            new TestQueuePolicyProvider(policy ?? TestQueuePolicyProvider.CreateDefault<T>()),
            NullLogger<RedisJobQueue<T>>.Instance);
    }

    public static string QueueKey<T>()
        => $"queue:{typeof(T).Name.ToLowerInvariant()}";

    public static string ShardQueueBaseKey<T>(int shardId)
        => $"{QueueKey<T>()}:shard:{shardId}";

    public static string QueueKey<T>(int shardId, int priority = 0)
        => priority switch
        {
            > 0 => $"{ShardQueueBaseKey<T>(shardId)}:high",
            < 0 => $"{ShardQueueBaseKey<T>(shardId)}:low",
            _ => ShardQueueBaseKey<T>(shardId)
        };

    public static string ProcessingQueueKey<T>()
        => $"{QueueKey<T>()}:processing";

    public static string ProcessingQueueKey<T>(int shardId)
        => $"{ShardQueueBaseKey<T>(shardId)}:processing";

    public static string DelayedQueueKey<T>()
        => $"{QueueKey<T>()}:delayed";

    public static string DelayedQueueKey<T>(int shardId)
        => $"{ShardQueueBaseKey<T>(shardId)}:delayed";

    public static string DeadLetterQueueKey<T>()
        => $"{QueueKey<T>()}:dlq";

    public static string DeadLetterQueueKey<T>(int shardId)
        => $"{ShardQueueBaseKey<T>(shardId)}:dlq";

    public static string LeaseKey<T>(string jobId)
        => $"{QueueKey<T>()}:lease:{jobId}";
}

internal sealed class TestQueuePolicyProvider : IQueuePolicyProvider
{
    private readonly Dictionary<string, ResolvedQueuePolicy> _policies;

    public TestQueuePolicyProvider(params ResolvedQueuePolicy[] policies)
    {
        _policies = policies.ToDictionary(policy => Normalize(policy.QueueName), StringComparer.OrdinalIgnoreCase);
    }

    public ResolvedQueuePolicy GetPolicy<T>()
        => GetPolicy(typeof(T).Name);

    public ResolvedQueuePolicy GetPolicy(string queueName)
        => _policies.TryGetValue(Normalize(queueName), out var policy)
            ? policy
            : CreateDefault(queueName);

    public static ResolvedQueuePolicy CreateDefault<T>()
        => CreateDefault(typeof(T).Name);

    public static ResolvedQueuePolicy CreateDefault(string queueName)
        => new()
        {
            QueueName = Normalize(queueName),
            ShardCount = 1,
            SubscribedShards = [0],
            VisibilityTimeoutSeconds = 300,
            MaxAttempts = 3,
            RetryBaseDelaySeconds = 5,
            MaxRetryDelaySeconds = 60,
            IdempotencyWindowSeconds = 3600
        };

    private static string Normalize(string queueName)
    {
        var normalized = queueName.Trim();
        if (normalized.StartsWith("queue:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["queue:".Length..];
        }

        return normalized.ToLowerInvariant();
    }
}
