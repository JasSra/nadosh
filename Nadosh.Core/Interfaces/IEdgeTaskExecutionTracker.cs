using Nadosh.Core.Models;

namespace Nadosh.Core.Interfaces;

public interface IEdgeTaskExecutionTracker
{
    Task RecordQueuedAsync(
        EdgeTaskClaimResponse claim,
        string localQueueName,
        string localJobReference,
        string summary,
        string? metadataJson = null,
        CancellationToken cancellationToken = default);

    Task RecordClaimFailureAsync(
        EdgeTaskClaimResponse claim,
        string errorMessage,
        bool requeueRecommended = false,
        string? metadataJson = null,
        CancellationToken cancellationToken = default);

    Task MarkCompletedAsync(
        string authorizedTaskId,
        string summary,
        string? metadataJson = null,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        string authorizedTaskId,
        string errorMessage,
        bool requeueRecommended = false,
        string? metadataJson = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EdgeTaskExecutionRecord>> GetPendingUploadsAsync(int maxCount, CancellationToken cancellationToken = default);
    Task MarkUploadedAsync(long recordId, CancellationToken cancellationToken = default);
    Task MarkUploadDeferredAsync(long recordId, string errorMessage, TimeSpan backoff, CancellationToken cancellationToken = default);
}
