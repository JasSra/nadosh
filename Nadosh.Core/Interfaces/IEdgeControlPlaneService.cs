using Nadosh.Core.Models;

namespace Nadosh.Core.Interfaces;

public interface IEdgeControlPlaneService
{
    Task<EdgeEnrollmentResponse> EnrollAsync(EdgeEnrollmentRequest request, string? remoteAddress, CancellationToken cancellationToken = default);
    Task<EdgeHeartbeatResponse> RecordHeartbeatAsync(EdgeHeartbeatRequest request, string? remoteAddress, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuthorizedTaskDescriptor>> GetPendingTasksAsync(string agentId, CancellationToken cancellationToken = default);
    Task<EdgeTaskClaimResponse> ClaimTaskAsync(string agentId, string taskId, EdgeTaskClaimRequest request, CancellationToken cancellationToken = default);
    Task<EdgeTaskUpdateResponse> CompleteTaskAsync(string agentId, string taskId, EdgeTaskCompletionRequest request, CancellationToken cancellationToken = default);
    Task<EdgeTaskUpdateResponse> FailTaskAsync(string agentId, string taskId, EdgeTaskFailureRequest request, CancellationToken cancellationToken = default);
}
