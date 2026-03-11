using Microsoft.EntityFrameworkCore;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Infrastructure.Data;

public class ObservationHandoffDispatchService : IObservationHandoffDispatchService
{
    private readonly NadoshDbContext _db;

    public ObservationHandoffDispatchService(NadoshDbContext db) => _db = db;

    public async Task<ObservationHandoffDispatchTransitionResult> ScheduleAsync(
        ObservationHandoffDispatchKind dispatchKind,
        long sourceObservationId,
        string batchId,
        string targetIp,
        int port,
        string protocol,
        string? serviceName,
        string workerId,
        CancellationToken cancellationToken = default)
    {
        var exists = await _db.ObservationHandoffDispatches
            .AnyAsync(d => d.DispatchKind == dispatchKind && d.SourceObservationId == sourceObservationId, cancellationToken);

        if (exists)
            return new ObservationHandoffDispatchTransitionResult
            {
                Status = ObservationHandoffDispatchTransitionStatus.NoOp,
                CurrentState = ObservationHandoffDispatchState.Queued
            };

        var dispatch = new ObservationHandoffDispatch
        {
            DispatchKind = dispatchKind,
            SourceObservationId = sourceObservationId,
            State = ObservationHandoffDispatchState.Queued,
            BatchId = batchId,
            TargetIp = targetIp,
            Port = port,
            Protocol = protocol,
            ServiceName = serviceName,
            ScheduledAt = DateTime.UtcNow,
            WorkerId = workerId,
            DeliveryCount = 0
        };

        _db.ObservationHandoffDispatches.Add(dispatch);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return new ObservationHandoffDispatchTransitionResult
            {
                Status = ObservationHandoffDispatchTransitionStatus.Applied,
                CurrentState = ObservationHandoffDispatchState.Queued
            };
        }
        catch (DbUpdateException)
        {
            // Race: another process inserted first
            return new ObservationHandoffDispatchTransitionResult
            {
                Status = ObservationHandoffDispatchTransitionStatus.NoOp,
                CurrentState = ObservationHandoffDispatchState.Queued
            };
        }
    }

    public async Task<ObservationHandoffDispatchTransitionResult> StartAsync(
        ObservationHandoffDispatchKind dispatchKind,
        long sourceObservationId,
        string batchId,
        string targetIp,
        int port,
        string protocol,
        string? serviceName,
        string workerId,
        CancellationToken cancellationToken = default)
    {
        var current = await _db.ObservationHandoffDispatches
            .Where(d => d.DispatchKind == dispatchKind && d.SourceObservationId == sourceObservationId)
            .Select(d => new { d.State, d.DeliveryCount, d.ProducedObservationId })
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null)
            return new ObservationHandoffDispatchTransitionResult { Status = ObservationHandoffDispatchTransitionStatus.NotFound };

        if (current.State == ObservationHandoffDispatchState.Completed)
            return new ObservationHandoffDispatchTransitionResult
            {
                Status = ObservationHandoffDispatchTransitionStatus.Rejected,
                PreviousState = current.State,
                CurrentState = current.State,
                ProducedObservationId = current.ProducedObservationId,
                Reason = "Dispatch already completed"
            };

        if (current.State == ObservationHandoffDispatchState.Processing)
            return new ObservationHandoffDispatchTransitionResult
            {
                Status = ObservationHandoffDispatchTransitionStatus.NoOp,
                PreviousState = current.State,
                CurrentState = current.State
            };

        if (current.State != ObservationHandoffDispatchState.Queued)
            return new ObservationHandoffDispatchTransitionResult
            {
                Status = ObservationHandoffDispatchTransitionStatus.Rejected,
                PreviousState = current.State,
                CurrentState = current.State,
                Reason = $"Cannot start a dispatch in state {current.State}"
            };

        var now = DateTime.UtcNow;
        var newDeliveryCount = current.DeliveryCount + 1;
        var updated = await _db.ObservationHandoffDispatches
            .Where(d => d.DispatchKind == dispatchKind
                     && d.SourceObservationId == sourceObservationId
                     && d.State == ObservationHandoffDispatchState.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.State, ObservationHandoffDispatchState.Processing)
                .SetProperty(d => d.StartedAt, now)
                .SetProperty(d => d.DeliveryCount, newDeliveryCount)
                .SetProperty(d => d.WorkerId, workerId),
                cancellationToken);

        return updated > 0
            ? new ObservationHandoffDispatchTransitionResult
            {
                Status = ObservationHandoffDispatchTransitionStatus.Applied,
                PreviousState = ObservationHandoffDispatchState.Queued,
                CurrentState = ObservationHandoffDispatchState.Processing
            }
            : new ObservationHandoffDispatchTransitionResult
            {
                Status = ObservationHandoffDispatchTransitionStatus.NoOp,
                PreviousState = current.State,
                CurrentState = current.State
            };
    }

    public async Task<ObservationHandoffDispatchTransitionResult> CompleteAsync(
        ObservationHandoffDispatchKind dispatchKind,
        long sourceObservationId,
        long producedObservationId,
        string workerId,
        CancellationToken cancellationToken = default)
    {
        var current = await _db.ObservationHandoffDispatches
            .Where(d => d.DispatchKind == dispatchKind && d.SourceObservationId == sourceObservationId)
            .Select(d => new { d.State })
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null)
            return new ObservationHandoffDispatchTransitionResult { Status = ObservationHandoffDispatchTransitionStatus.NotFound };

        if (current.State != ObservationHandoffDispatchState.Processing)
            return new ObservationHandoffDispatchTransitionResult
            {
                Status = ObservationHandoffDispatchTransitionStatus.Rejected,
                PreviousState = current.State,
                CurrentState = current.State,
                Reason = $"Cannot complete a dispatch in state {current.State}"
            };

        var now = DateTime.UtcNow;
        var updated = await _db.ObservationHandoffDispatches
            .Where(d => d.DispatchKind == dispatchKind
                     && d.SourceObservationId == sourceObservationId
                     && d.State == ObservationHandoffDispatchState.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.State, ObservationHandoffDispatchState.Completed)
                .SetProperty(d => d.CompletedAt, now)
                .SetProperty(d => d.ProducedObservationId, producedObservationId)
                .SetProperty(d => d.WorkerId, workerId),
                cancellationToken);

        return updated > 0
            ? new ObservationHandoffDispatchTransitionResult
            {
                Status = ObservationHandoffDispatchTransitionStatus.Applied,
                PreviousState = ObservationHandoffDispatchState.Processing,
                CurrentState = ObservationHandoffDispatchState.Completed,
                ProducedObservationId = producedObservationId
            }
            : new ObservationHandoffDispatchTransitionResult
            {
                Status = ObservationHandoffDispatchTransitionStatus.Rejected,
                PreviousState = current.State,
                CurrentState = current.State,
                Reason = "Concurrent state modification detected"
            };
    }

    public async Task<ObservationHandoffDispatchTransitionResult> FailAsync(
        ObservationHandoffDispatchKind dispatchKind,
        long sourceObservationId,
        string workerId,
        string error,
        long? producedObservationId = null,
        CancellationToken cancellationToken = default)
    {
        var current = await _db.ObservationHandoffDispatches
            .Where(d => d.DispatchKind == dispatchKind && d.SourceObservationId == sourceObservationId)
            .Select(d => new { d.State })
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null)
            return new ObservationHandoffDispatchTransitionResult { Status = ObservationHandoffDispatchTransitionStatus.NotFound };

        var now = DateTime.UtcNow;
        var updated = await _db.ObservationHandoffDispatches
            .Where(d => d.DispatchKind == dispatchKind && d.SourceObservationId == sourceObservationId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.State, ObservationHandoffDispatchState.Error)
                .SetProperty(d => d.CompletedAt, now)
                .SetProperty(d => d.LastError, error)
                .SetProperty(d => d.ProducedObservationId, producedObservationId)
                .SetProperty(d => d.WorkerId, workerId),
                cancellationToken);

        return updated > 0
            ? new ObservationHandoffDispatchTransitionResult
            {
                Status = ObservationHandoffDispatchTransitionStatus.Applied,
                PreviousState = current.State,
                CurrentState = ObservationHandoffDispatchState.Error,
                ProducedObservationId = producedObservationId
            }
            : new ObservationHandoffDispatchTransitionResult { Status = ObservationHandoffDispatchTransitionStatus.NotFound };
    }
}
