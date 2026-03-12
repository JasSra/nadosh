using Nadosh.Core.Models;

namespace Nadosh.Core.Interfaces;

public interface IAssessmentToolCatalog
{
    IReadOnlyList<AssessmentToolDefinition> GetAll();
    AssessmentToolDefinition? GetById(string toolId);
    bool IsRegistered(string toolId);
}
