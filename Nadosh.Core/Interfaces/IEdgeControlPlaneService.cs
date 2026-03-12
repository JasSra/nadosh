using Nadosh.Core.Models;

namespace Nadosh.Core.Interfaces;

public interface IEdgeControlPlaneService
{
    Task<EdgeEnrollmentResponse> EnrollAsync(EdgeEnrollmentRequest request, string? remoteAddress, CancellationToken cancellationToken = default);
    Task<EdgeHeartbeatResponse> RecordHeartbeatAsync(EdgeHeartbeatRequest request, string? remoteAddress, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuthorizedTaskDescriptor>> GetPendingTasksAsync(string agentId, CancellationToken cancellationToken = default);
}
