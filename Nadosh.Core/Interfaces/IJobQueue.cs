using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Nadosh.Core.Interfaces;

public sealed class JobEnqueueOptions
{
    public string? ShardKey { get; init; }
}

public class JobQueueMessage<T>
{
    public string JobId { get; set; } = Guid.NewGuid().ToString();
    public T Payload { get; set; } = default!;
    public int ShardId { get; set; }
    public int Priority { get; set; }
    public int AttemptCount { get; set; }
    public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;
    public string? IdempotencyKey { get; set; }
    public string? LeaseToken { get; set; }

    [JsonIgnore]
    public string? ProcessingPayload { get; set; }
}

public interface IJobQueue<T>
{
    Task EnqueueAsync(T payload, string? idempotencyKey = null, int priority = 0, CancellationToken cancellationToken = default);
    Task EnqueueAsync(T payload, string? idempotencyKey, int priority, JobEnqueueOptions? enqueueOptions, CancellationToken cancellationToken = default);
    Task EnqueueDelayedAsync(T payload, TimeSpan delay, string? idempotencyKey = null, int priority = 0, CancellationToken cancellationToken = default);
    Task EnqueueDelayedAsync(T payload, TimeSpan delay, string? idempotencyKey, int priority, JobEnqueueOptions? enqueueOptions, CancellationToken cancellationToken = default);
    Task<JobQueueMessage<T>?> DequeueAsync(TimeSpan visibilityTimeout, CancellationToken cancellationToken = default);
    Task AcknowledgeAsync(JobQueueMessage<T> message, CancellationToken cancellationToken = default);
    Task RejectAsync(JobQueueMessage<T> message, bool reenqueue = true, TimeSpan? reenqueueDelay = null, CancellationToken cancellationToken = default);
    Task DeadLetterAsync(JobQueueMessage<T> message, string errorReason, CancellationToken cancellationToken = default);
    Task RenewLeaseAsync(JobQueueMessage<T> message, TimeSpan additionalTimeout, CancellationToken cancellationToken = default);
}
