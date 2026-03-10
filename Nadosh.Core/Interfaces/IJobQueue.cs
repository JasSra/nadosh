using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nadosh.Core.Interfaces;

public class JobQueueMessage<T>
{
    public string JobId { get; set; } = Guid.NewGuid().ToString();
    public T Payload { get; set; } = default!;
    public int AttemptCount { get; set; }
    public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;
    public string? LeaseToken { get; set; }
}

public interface IJobQueue<T>
{
    Task EnqueueAsync(T payload, string? idempotencyKey = null, int priority = 0, CancellationToken cancellationToken = default);
    Task EnqueueDelayedAsync(T payload, TimeSpan delay, string? idempotencyKey = null, CancellationToken cancellationToken = default);
    Task<JobQueueMessage<T>?> DequeueAsync(TimeSpan visibilityTimeout, CancellationToken cancellationToken = default);
    Task AcknowledgeAsync(JobQueueMessage<T> message, CancellationToken cancellationToken = default);
    Task RejectAsync(JobQueueMessage<T> message, bool reenqueue = true, CancellationToken cancellationToken = default);
    Task DeadLetterAsync(JobQueueMessage<T> message, string errorReason, CancellationToken cancellationToken = default);
    Task RenewLeaseAsync(JobQueueMessage<T> message, TimeSpan additionalTimeout, CancellationToken cancellationToken = default);
}
