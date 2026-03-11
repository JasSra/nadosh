using Microsoft.Extensions.Options;
using Nadosh.Core.Configuration;
using Nadosh.Core.Interfaces;

namespace Nadosh.Infrastructure.Queue;

public sealed class QueuePolicyProvider : IQueuePolicyProvider
{
    private const int DefaultShardCount = 1;
    private const int DefaultVisibilityTimeoutSeconds = 300;
    private const int DefaultMaxAttempts = 3;
    private const int DefaultRetryBaseDelaySeconds = 5;
    private const int DefaultMaxRetryDelaySeconds = 60;
    private const int DefaultIdempotencyWindowSeconds = 3600;

    private readonly IOptionsMonitor<QueueTransportOptions> _optionsMonitor;

    public QueuePolicyProvider(IOptionsMonitor<QueueTransportOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public ResolvedQueuePolicy GetPolicy<T>()
        => GetPolicy(typeof(T).Name.ToLowerInvariant());

    public ResolvedQueuePolicy GetPolicy(string queueName)
    {
        var normalizedQueueName = NormalizeQueueName(queueName);
        var options = _optionsMonitor.CurrentValue ?? new QueueTransportOptions();
        options.Queues.TryGetValue(normalizedQueueName, out var queueOverrides);

        var defaultPolicy = options.Default ?? new QueuePolicyOptions();

        var shardCount = ResolvePositiveInt(
            queueOverrides?.ShardCount,
            defaultPolicy.ShardCount,
            DefaultShardCount);

        var visibilityTimeoutSeconds = ResolvePositiveInt(
            queueOverrides?.VisibilityTimeoutSeconds,
            defaultPolicy.VisibilityTimeoutSeconds,
            DefaultVisibilityTimeoutSeconds);

        var maxAttempts = ResolvePositiveInt(
            queueOverrides?.MaxAttempts,
            defaultPolicy.MaxAttempts,
            DefaultMaxAttempts);

        var retryBaseDelaySeconds = ResolvePositiveInt(
            queueOverrides?.RetryBaseDelaySeconds,
            defaultPolicy.RetryBaseDelaySeconds,
            DefaultRetryBaseDelaySeconds);

        var maxRetryDelaySeconds = ResolvePositiveInt(
            queueOverrides?.MaxRetryDelaySeconds,
            defaultPolicy.MaxRetryDelaySeconds,
            DefaultMaxRetryDelaySeconds);

        if (maxRetryDelaySeconds < retryBaseDelaySeconds)
        {
            maxRetryDelaySeconds = retryBaseDelaySeconds;
        }

        var idempotencyWindowSeconds = ResolvePositiveInt(
            queueOverrides?.IdempotencyWindowSeconds,
            defaultPolicy.IdempotencyWindowSeconds,
            DefaultIdempotencyWindowSeconds);

        var subscribedShards = ResolveSubscribedShards(shardCount, options.SubscribedShards);

        return new ResolvedQueuePolicy
        {
            QueueName = normalizedQueueName,
            ShardCount = shardCount,
            SubscribedShards = subscribedShards,
            VisibilityTimeoutSeconds = visibilityTimeoutSeconds,
            MaxAttempts = maxAttempts,
            RetryBaseDelaySeconds = retryBaseDelaySeconds,
            MaxRetryDelaySeconds = maxRetryDelaySeconds,
            IdempotencyWindowSeconds = idempotencyWindowSeconds
        };
    }

    private static int ResolvePositiveInt(int? primary, int? fallback, int hardDefault)
    {
        if (primary.HasValue && primary.Value > 0)
        {
            return primary.Value;
        }

        if (fallback.HasValue && fallback.Value > 0)
        {
            return fallback.Value;
        }

        return hardDefault;
    }

    private static IReadOnlyList<int> ResolveSubscribedShards(int shardCount, IReadOnlyCollection<int>? configuredShards)
    {
        if (shardCount <= 1)
        {
            return [0];
        }

        var normalized = configuredShards?
            .Where(shard => shard >= 0)
            .Select(shard => shard % shardCount)
            .Distinct()
            .OrderBy(shard => shard)
            .ToArray();

        if (normalized is { Length: > 0 })
        {
            return normalized;
        }

        return Enumerable.Range(0, shardCount).ToArray();
    }

    private static string NormalizeQueueName(string queueName)
    {
        var normalized = queueName.Trim();
        if (normalized.StartsWith("queue:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["queue:".Length..];
        }

        return normalized.ToLowerInvariant();
    }
}