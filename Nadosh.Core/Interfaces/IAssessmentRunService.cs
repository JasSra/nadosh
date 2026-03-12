using Nadosh.Core.Models;

namespace Nadosh.Core.Interfaces;

public interface IAssessmentRunService
{
    Task<AssessmentRunSubmissionResult> SubmitAsync(AssessmentRunSubmissionRequest request, CancellationToken cancellationToken = default);
}
