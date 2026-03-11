using Nadosh.Core.Models;

namespace Nadosh.Core.Interfaces;

public interface IStage1DispatchStateService
{
    Task<Stage1DispatchTransitionResult> ScheduleAsync(
        string batchId,
        string targetIp,
        IReadOnlyCollection<int> ports,
        string workerId,
        CancellationToken cancellationToken = default);

    Task<Stage1DispatchTransitionResult> StartAsync(
        string batchId,
        string targetIp,
        IReadOnlyCollection<int> ports,
        string workerId,
        CancellationToken cancellationToken = default);

    Task<Stage1DispatchTransitionResult> CompleteAsync(
        string batchId,
        string targetIp,
        string workerId,
        IReadOnlyCollection<Observation> observations,
        CancellationToken cancellationToken = default);

    Task<Stage1DispatchTransitionResult> FailAsync(
        string batchId,
        string targetIp,
        string workerId,
        string error,
        CancellationToken cancellationToken = default);
}
