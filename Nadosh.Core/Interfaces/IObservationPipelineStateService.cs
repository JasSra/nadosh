using Nadosh.Core.Models;

namespace Nadosh.Core.Interfaces;

public interface IObservationPipelineStateService
{
    Task<ObservationPipelineTransitionResult> TransitionAsync(
        long observationId,
        ObservationPipelineState newState,
        string workerId,
        CancellationToken cancellationToken = default);

    Task<ObservationPipelineTransitionResult> RetryAsync(
        long observationId,
        ObservationPipelineState reentryState,
        string workerId,
        CancellationToken cancellationToken = default);
}