using Microsoft.EntityFrameworkCore;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Infrastructure.Data;

public class ObservationPipelineStateService : IObservationPipelineStateService
{
    private static readonly IReadOnlyDictionary<ObservationPipelineState, IReadOnlySet<ObservationPipelineState>> ValidTransitions =
        new Dictionary<ObservationPipelineState, IReadOnlySet<ObservationPipelineState>>
        {
            [ObservationPipelineState.Scheduled]            = new HashSet<ObservationPipelineState> { ObservationPipelineState.Stage1Scanning, ObservationPipelineState.Error, ObservationPipelineState.Suppressed },
            [ObservationPipelineState.Stage1Scanning]       = new HashSet<ObservationPipelineState> { ObservationPipelineState.Stage1Observed, ObservationPipelineState.Error, ObservationPipelineState.Suppressed },
            [ObservationPipelineState.Stage1Observed]       = new HashSet<ObservationPipelineState> { ObservationPipelineState.Classified, ObservationPipelineState.Error, ObservationPipelineState.Suppressed },
            [ObservationPipelineState.Classified]           = new HashSet<ObservationPipelineState> { ObservationPipelineState.FlaggedForEnrichment, ObservationPipelineState.Completed, ObservationPipelineState.Error, ObservationPipelineState.Suppressed },
            [ObservationPipelineState.FlaggedForEnrichment] = new HashSet<ObservationPipelineState> { ObservationPipelineState.Stage2Queued, ObservationPipelineState.Error, ObservationPipelineState.Suppressed },
            [ObservationPipelineState.Stage2Queued]         = new HashSet<ObservationPipelineState> { ObservationPipelineState.Stage2Processing, ObservationPipelineState.Error, ObservationPipelineState.Suppressed },
            [ObservationPipelineState.Stage2Processing]     = new HashSet<ObservationPipelineState> { ObservationPipelineState.Enriched, ObservationPipelineState.Error, ObservationPipelineState.Suppressed },
            [ObservationPipelineState.Enriched]             = new HashSet<ObservationPipelineState> { ObservationPipelineState.Completed, ObservationPipelineState.Error, ObservationPipelineState.Suppressed },
            [ObservationPipelineState.Error]                = new HashSet<ObservationPipelineState> { ObservationPipelineState.Error, ObservationPipelineState.Suppressed },
            [ObservationPipelineState.Suppressed]           = new HashSet<ObservationPipelineState> { ObservationPipelineState.Error, ObservationPipelineState.Suppressed },
            [ObservationPipelineState.Completed]            = new HashSet<ObservationPipelineState> { ObservationPipelineState.Error, ObservationPipelineState.Suppressed },
        };

    private readonly NadoshDbContext _db;

    public ObservationPipelineStateService(NadoshDbContext db) => _db = db;

    public async Task<ObservationPipelineTransitionResult> TransitionAsync(
        long observationId,
        ObservationPipelineState newState,
        string workerId,
        CancellationToken cancellationToken = default)
    {
        var current = await _db.Observations
            .Where(o => o.Id == observationId)
            .Select(o => new { o.PipelineState })
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null)
            return new ObservationPipelineTransitionResult { Status = ObservationPipelineTransitionStatus.NotFound };

        if (current.PipelineState == newState)
            return new ObservationPipelineTransitionResult
            {
                Status = ObservationPipelineTransitionStatus.NoOp,
                PreviousState = current.PipelineState,
                CurrentState = newState
            };

        // Any state can transition to Error or Suppressed
        bool isValid = newState is ObservationPipelineState.Error or ObservationPipelineState.Suppressed
            || (current.PipelineState.HasValue
                && ValidTransitions.TryGetValue(current.PipelineState.Value, out var allowed)
                && allowed.Contains(newState));

        if (!isValid)
            return new ObservationPipelineTransitionResult
            {
                Status = ObservationPipelineTransitionStatus.Rejected,
                PreviousState = current.PipelineState,
                CurrentState = current.PipelineState,
                Reason = $"Transition from {current.PipelineState?.ToString() ?? "null"} to {newState} is not allowed"
            };

        var now = DateTime.UtcNow;
        var updated = await _db.Observations
            .Where(o => o.Id == observationId && o.PipelineState == current.PipelineState)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.PipelineState, newState)
                .SetProperty(o => o.PipelineStateChangedAt, now)
                .SetProperty(o => o.PipelineWorkerId, workerId),
                cancellationToken);

        return updated > 0
            ? new ObservationPipelineTransitionResult
            {
                Status = ObservationPipelineTransitionStatus.Applied,
                PreviousState = current.PipelineState,
                CurrentState = newState
            }
            : new ObservationPipelineTransitionResult
            {
                Status = ObservationPipelineTransitionStatus.Rejected,
                PreviousState = current.PipelineState,
                CurrentState = current.PipelineState,
                Reason = "Concurrent state modification detected"
            };
    }

    public async Task<ObservationPipelineTransitionResult> RetryAsync(
        long observationId,
        ObservationPipelineState reentryState,
        string workerId,
        CancellationToken cancellationToken = default)
    {
        var current = await _db.Observations
            .Where(o => o.Id == observationId)
            .Select(o => new { o.PipelineState, o.PipelineRetryCount })
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null)
            return new ObservationPipelineTransitionResult { Status = ObservationPipelineTransitionStatus.NotFound };

        if (current.PipelineState != ObservationPipelineState.Error)
            return new ObservationPipelineTransitionResult
            {
                Status = ObservationPipelineTransitionStatus.Rejected,
                PreviousState = current.PipelineState,
                CurrentState = current.PipelineState,
                Reason = $"Retry is only allowed from Error state; current state is {current.PipelineState?.ToString() ?? "null"}"
            };

        var now = DateTime.UtcNow;
        var newRetryCount = current.PipelineRetryCount + 1;
        var updated = await _db.Observations
            .Where(o => o.Id == observationId && o.PipelineState == ObservationPipelineState.Error)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.PipelineState, reentryState)
                .SetProperty(o => o.PipelineStateChangedAt, now)
                .SetProperty(o => o.PipelineWorkerId, workerId)
                .SetProperty(o => o.PipelineRetryCount, newRetryCount),
                cancellationToken);

        return updated > 0
            ? new ObservationPipelineTransitionResult
            {
                Status = ObservationPipelineTransitionStatus.Applied,
                PreviousState = ObservationPipelineState.Error,
                CurrentState = reentryState
            }
            : new ObservationPipelineTransitionResult
            {
                Status = ObservationPipelineTransitionStatus.Rejected,
                PreviousState = current.PipelineState,
                CurrentState = current.PipelineState,
                Reason = "Concurrent state modification detected"
            };
    }
}
