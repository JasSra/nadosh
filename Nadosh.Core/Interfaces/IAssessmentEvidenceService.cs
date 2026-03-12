using Nadosh.Core.Models;

namespace Nadosh.Core.Interfaces;

public interface IAssessmentEvidenceService
{
    Task<AssessmentEvidenceBundle?> BuildAsync(string runId, CancellationToken cancellationToken = default);
}
