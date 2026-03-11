namespace Nadosh.Core.Configuration;

public sealed class QueueTransportOptions
{
    public const string SectionName = "QueueTransport";

    public List<int> SubscribedShards { get; set; } = [];

    public QueuePolicyOptions Default { get; set; } = new();

    public Dictionary<string, QueuePolicyOptions> Queues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class QueuePolicyOptions
{
    public int? ShardCount { get; set; }

    public int? VisibilityTimeoutSeconds { get; set; }

    public int? MaxAttempts { get; set; }

    public int? RetryBaseDelaySeconds { get; set; }

    public int? MaxRetryDelaySeconds { get; set; }

    public int? IdempotencyWindowSeconds { get; set; }
}

public sealed class ResolvedQueuePolicy
{
    public required string QueueName { get; init; }

    public int ShardCount { get; init; } = 1;

    public IReadOnlyList<int> SubscribedShards { get; init; } = Array.Empty<int>();

    public required int VisibilityTimeoutSeconds { get; init; }

    public required int MaxAttempts { get; init; }

    public required int RetryBaseDelaySeconds { get; init; }

    public required int MaxRetryDelaySeconds { get; init; }

    public required int IdempotencyWindowSeconds { get; init; }

    public TimeSpan VisibilityTimeout => TimeSpan.FromSeconds(VisibilityTimeoutSeconds);

    public TimeSpan RetryBaseDelay => TimeSpan.FromSeconds(RetryBaseDelaySeconds);

    public TimeSpan MaxRetryDelay => TimeSpan.FromSeconds(MaxRetryDelaySeconds);

    public TimeSpan IdempotencyWindow => TimeSpan.FromSeconds(IdempotencyWindowSeconds);
}