using Nadosh.Core.Models;

namespace Nadosh.Core.Interfaces;

public interface IAssessmentRunRepository
{
    Task<AssessmentRun?> GetByIdAsync(string runId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssessmentRun>> GetByStatusAsync(AssessmentRunStatus status, int take = 100, CancellationToken cancellationToken = default);
    Task CreateAsync(AssessmentRun run, CancellationToken cancellationToken = default);
    Task UpdateAsync(AssessmentRun run, CancellationToken cancellationToken = default);
}
