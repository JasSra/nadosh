using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Infrastructure.Data;

public class Stage1DispatchStateService : IStage1DispatchStateService
{
    private readonly NadoshDbContext _db;

    public Stage1DispatchStateService(NadoshDbContext db) => _db = db;

    public async Task<Stage1DispatchTransitionResult> ScheduleAsync(
        string batchId,
        string targetIp,
        IReadOnlyCollection<int> ports,
        string workerId,
        CancellationToken cancellationToken = default)
    {
        var exists = await _db.Stage1Dispatches
            .AnyAsync(d => d.BatchId == batchId && d.TargetIp == targetIp, cancellationToken);

        if (exists)
            return new Stage1DispatchTransitionResult
            {
                Status = Stage1DispatchTransitionStatus.NoOp,
                CurrentState = Stage1DispatchState.Scheduled
            };

        var dispatch = new Stage1Dispatch
        {
            BatchId = batchId,
            TargetIp = targetIp,
            State = Stage1DispatchState.Scheduled,
            ScheduledAt = DateTime.UtcNow,
            WorkerId = workerId,
            PortsJson = JsonSerializer.Serialize(ports),
            DeliveryCount = 0
        };

        _db.Stage1Dispatches.Add(dispatch);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return new Stage1DispatchTransitionResult
            {
                Status = Stage1DispatchTransitionStatus.Applied,
                CurrentState = Stage1DispatchState.Scheduled
            };
        }
        catch (DbUpdateException)
        {
            // Race: another process inserted first
            return new Stage1DispatchTransitionResult
            {
                Status = Stage1DispatchTransitionStatus.NoOp,
                CurrentState = Stage1DispatchState.Scheduled
            };
        }
    }

    public async Task<Stage1DispatchTransitionResult> StartAsync(
        string batchId,
        string targetIp,
        IReadOnlyCollection<int> ports,
        string workerId,
        CancellationToken cancellationToken = default)
    {
        var current = await _db.Stage1Dispatches
            .Where(d => d.BatchId == batchId && d.TargetIp == targetIp)
            .Select(d => new { d.State, d.DeliveryCount })
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null)
            return new Stage1DispatchTransitionResult { Status = Stage1DispatchTransitionStatus.NotFound };

        if (current.State == Stage1DispatchState.Stage1Scanning)
            return new Stage1DispatchTransitionResult
            {
                Status = Stage1DispatchTransitionStatus.NoOp,
                PreviousState = current.State,
                CurrentState = current.State
            };

        if (current.State != Stage1DispatchState.Scheduled)
            return new Stage1DispatchTransitionResult
            {
                Status = Stage1DispatchTransitionStatus.Rejected,
                PreviousState = current.State,
                CurrentState = current.State,
                Reason = $"Cannot start a dispatch in state {current.State}"
            };

        var now = DateTime.UtcNow;
        var newDeliveryCount = current.DeliveryCount + 1;
        var updated = await _db.Stage1Dispatches
            .Where(d => d.BatchId == batchId && d.TargetIp == targetIp && d.State == Stage1DispatchState.Scheduled)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.State, Stage1DispatchState.Stage1Scanning)
                .SetProperty(d => d.StartedAt, now)
                .SetProperty(d => d.DeliveryCount, newDeliveryCount)
                .SetProperty(d => d.WorkerId, workerId),
                cancellationToken);

        return updated > 0
            ? new Stage1DispatchTransitionResult
            {
                Status = Stage1DispatchTransitionStatus.Applied,
                PreviousState = Stage1DispatchState.Scheduled,
                CurrentState = Stage1DispatchState.Stage1Scanning
            }
            : new Stage1DispatchTransitionResult
            {
                Status = Stage1DispatchTransitionStatus.NoOp,
                PreviousState = current.State,
                CurrentState = current.State
            };
    }

    public async Task<Stage1DispatchTransitionResult> CompleteAsync(
        string batchId,
        string targetIp,
        string workerId,
        IReadOnlyCollection<Observation> observations,
        CancellationToken cancellationToken = default)
    {
        var current = await _db.Stage1Dispatches
            .Where(d => d.BatchId == batchId && d.TargetIp == targetIp)
            .Select(d => new { d.State })
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null)
            return new Stage1DispatchTransitionResult { Status = Stage1DispatchTransitionStatus.NotFound };

        if (current.State != Stage1DispatchState.Stage1Scanning)
            return new Stage1DispatchTransitionResult
            {
                Status = Stage1DispatchTransitionStatus.Rejected,
                PreviousState = current.State,
                CurrentState = current.State,
                Reason = $"Cannot complete a dispatch in state {current.State}"
            };

        var now = DateTime.UtcNow;
        var observationCount = observations.Count;
        var openCount = observations.Count(o => string.Equals(o.State, "open", StringComparison.OrdinalIgnoreCase));

        var updated = await _db.Stage1Dispatches
            .Where(d => d.BatchId == batchId && d.TargetIp == targetIp && d.State == Stage1DispatchState.Stage1Scanning)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.State, Stage1DispatchState.Stage1Observed)
                .SetProperty(d => d.CompletedAt, now)
                .SetProperty(d => d.ObservationLinkedAt, now)
                .SetProperty(d => d.ObservationCount, observationCount)
                .SetProperty(d => d.OpenObservationCount, openCount)
                .SetProperty(d => d.WorkerId, workerId),
                cancellationToken);

        return updated > 0
            ? new Stage1DispatchTransitionResult
            {
                Status = Stage1DispatchTransitionStatus.Applied,
                PreviousState = Stage1DispatchState.Stage1Scanning,
                CurrentState = Stage1DispatchState.Stage1Observed
            }
            : new Stage1DispatchTransitionResult
            {
                Status = Stage1DispatchTransitionStatus.Rejected,
                PreviousState = current.State,
                CurrentState = current.State,
                Reason = "Concurrent state modification detected"
            };
    }

    public async Task<Stage1DispatchTransitionResult> FailAsync(
        string batchId,
        string targetIp,
        string workerId,
        string error,
        CancellationToken cancellationToken = default)
    {
        var current = await _db.Stage1Dispatches
            .Where(d => d.BatchId == batchId && d.TargetIp == targetIp)
            .Select(d => new { d.State })
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null)
            return new Stage1DispatchTransitionResult { Status = Stage1DispatchTransitionStatus.NotFound };

        var now = DateTime.UtcNow;
        var updated = await _db.Stage1Dispatches
            .Where(d => d.BatchId == batchId && d.TargetIp == targetIp)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.State, Stage1DispatchState.Error)
                .SetProperty(d => d.CompletedAt, now)
                .SetProperty(d => d.LastError, error)
                .SetProperty(d => d.WorkerId, workerId),
                cancellationToken);

        return updated > 0
            ? new Stage1DispatchTransitionResult
            {
                Status = Stage1DispatchTransitionStatus.Applied,
                PreviousState = current.State,
                CurrentState = Stage1DispatchState.Error
            }
            : new Stage1DispatchTransitionResult { Status = Stage1DispatchTransitionStatus.NotFound };
    }
}
