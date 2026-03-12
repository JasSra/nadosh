using Nadosh.Core.Models;

namespace Nadosh.Core.Interfaces;

public interface IAssessmentPolicyService
{
    AssessmentPolicyEvaluation Evaluate(AssessmentPolicyEvaluationRequest request);
}
