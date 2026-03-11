using Nadosh.Core.Models;

namespace Nadosh.Core.Interfaces;

public interface IObservationHandoffDispatchService
{
    Task<ObservationHandoffDispatchTransitionResult> ScheduleAsync(
        ObservationHandoffDispatchKind dispatchKind,
        long sourceObservationId,
        string batchId,
        string targetIp,
        int port,
        string protocol,
        string? serviceName,
        string workerId,
        CancellationToken cancellationToken = default);

    Task<ObservationHandoffDispatchTransitionResult> StartAsync(
        ObservationHandoffDispatchKind dispatchKind,
        long sourceObservationId,
        string batchId,
        string targetIp,
        int port,
        string protocol,
        string? serviceName,
        string workerId,
        CancellationToken cancellationToken = default);

    Task<ObservationHandoffDispatchTransitionResult> CompleteAsync(
        ObservationHandoffDispatchKind dispatchKind,
        long sourceObservationId,
        long producedObservationId,
        string workerId,
        CancellationToken cancellationToken = default);

    Task<ObservationHandoffDispatchTransitionResult> FailAsync(
        ObservationHandoffDispatchKind dispatchKind,
        long sourceObservationId,
        string workerId,
        string error,
        long? producedObservationId = null,
        CancellationToken cancellationToken = default);
}