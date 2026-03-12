using Nadosh.Core.Models;

namespace Nadosh.Core.Interfaces;

public interface IMicrosoftAgentAssessmentAdapter
{
    Task<MicrosoftAgentAssessmentContext?> BuildContextAsync(string runId, CancellationToken cancellationToken = default);
}
