using Nadosh.Core.Interfaces;
using Nadosh.Core.Configuration;

namespace Nadosh.Workers.Queue;

internal static class QueueProcessingUtilities
{
    public static async Task RunWithLeaseHeartbeatAsync<T>(
        IJobQueue<T> queue,
        JobQueueMessage<T> message,
        ResolvedQueuePolicy policy,
        Func<CancellationToken, Task> processAsync,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewTask = RenewLeaseUntilCompletedAsync(
            queue,
            message,
            policy,
            logger,
            renewalCts.Token);

        try
        {
            await processAsync(cancellationToken);
        }
        finally
        {
            renewalCts.Cancel();

            try
            {
                await renewTask;
            }
            catch (OperationCanceledException) when (renewalCts.IsCancellationRequested)
            {
            }
        }
    }

    public static TimeSpan ComputeRetryDelay(int attemptCount, ResolvedQueuePolicy policy)
    {
        var exponent = Math.Max(0, attemptCount - 1);
        var delaySeconds = policy.RetryBaseDelay.TotalSeconds * Math.Pow(2, exponent);
        return TimeSpan.FromSeconds(Math.Min(policy.MaxRetryDelay.TotalSeconds, delaySeconds));
    }

    public static async Task RejectWithBackoffOrDeadLetterAsync<T>(
        IJobQueue<T> queue,
        JobQueueMessage<T> message,
        Exception exception,
        ILogger logger,
        ResolvedQueuePolicy policy,
        CancellationToken cancellationToken,
        int? maxAttempts = null)
    {
        var resolvedMaxAttempts = maxAttempts ?? policy.MaxAttempts;
        if (message.AttemptCount >= resolvedMaxAttempts)
        {
            await queue.DeadLetterAsync(message, exception.Message, cancellationToken);
            return;
        }

        var retryDelay = ComputeRetryDelay(message.AttemptCount, policy);
        logger.LogInformation(
            "Retrying job {JobId} after {DelaySeconds}s because attempt {Attempt} failed: {Error}",
            message.JobId,
            retryDelay.TotalSeconds,
            message.AttemptCount,
            exception.Message);

        await queue.RejectAsync(message, reenqueue: true, reenqueueDelay: retryDelay, cancellationToken);
    }

    private static async Task RenewLeaseUntilCompletedAsync<T>(
        IJobQueue<T> queue,
        JobQueueMessage<T> message,
        ResolvedQueuePolicy policy,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var renewalCadence = ComputeRenewalCadence(policy.VisibilityTimeout);
        using var timer = new PeriodicTimer(renewalCadence);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await queue.RenewLeaseAsync(message, policy.VisibilityTimeout, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to renew lease for job {JobId}; worker will continue and ownership checks will guard ack/reject/dead-letter.",
                    message.JobId);
            }
        }
    }

    private static TimeSpan ComputeRenewalCadence(TimeSpan visibilityTimeout)
    {
        var halfWindow = TimeSpan.FromTicks(visibilityTimeout.Ticks / 2);
        if (halfWindow < TimeSpan.FromSeconds(5))
        {
            return TimeSpan.FromSeconds(5);
        }

        return halfWindow;
    }
}