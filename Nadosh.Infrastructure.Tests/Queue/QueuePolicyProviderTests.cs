using Microsoft.Extensions.Options;
using Nadosh.Core.Configuration;
using Nadosh.Infrastructure.Queue;

namespace Nadosh.Infrastructure.Tests.Queue;

public sealed class QueuePolicyProviderTests
{
    [Fact]
    public void GetPolicy_AppliesQueueOverridesWithDefaultFallbacks()
    {
        var options = new QueueTransportOptions
        {
            Default = new QueuePolicyOptions
            {
                VisibilityTimeoutSeconds = 300,
                MaxAttempts = 5,
                RetryBaseDelaySeconds = 5,
                MaxRetryDelaySeconds = 45,
                IdempotencyWindowSeconds = 3600
            },
            Queues = new Dictionary<string, QueuePolicyOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["stage2enrichmentjob"] = new()
                {
                    VisibilityTimeoutSeconds = 600,
                    MaxAttempts = 4,
                    MaxRetryDelaySeconds = 120
                }
            }
        };

        var provider = new QueuePolicyProvider(new StaticOptionsMonitor<QueueTransportOptions>(options));

        var policy = provider.GetPolicy("stage2enrichmentjob");

        Assert.Equal("stage2enrichmentjob", policy.QueueName);
        Assert.Equal(600, policy.VisibilityTimeoutSeconds);
        Assert.Equal(4, policy.MaxAttempts);
        Assert.Equal(5, policy.RetryBaseDelaySeconds);
        Assert.Equal(120, policy.MaxRetryDelaySeconds);
        Assert.Equal(3600, policy.IdempotencyWindowSeconds);
        Assert.Equal(1, policy.ShardCount);
        Assert.Equal([0], policy.SubscribedShards);
    }

    [Fact]
    public void GetPolicy_NormalizesConfiguredShardSubscriptions()
    {
        var options = new QueueTransportOptions
        {
            SubscribedShards = [-1, 1, 3, 7, 7],
            Default = new QueuePolicyOptions
            {
                ShardCount = 4,
                VisibilityTimeoutSeconds = 300,
                MaxAttempts = 5,
                RetryBaseDelaySeconds = 5,
                MaxRetryDelaySeconds = 45,
                IdempotencyWindowSeconds = 3600
            }
        };

        var provider = new QueuePolicyProvider(new StaticOptionsMonitor<QueueTransportOptions>(options));

        var policy = provider.GetPolicy("fingerprintjob");

        Assert.Equal(4, policy.ShardCount);
        Assert.Equal([1, 3], policy.SubscribedShards);
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name)
            => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener)
            => null;
    }
}