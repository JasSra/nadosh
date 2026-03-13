using Nadosh.Core.Models;

namespace Nadosh.Agents.Models;

/// <summary>
/// Context provided to the Parse and Plan agent for decision making.
/// </summary>
public class AgentPlanningContext
{
    public required string AssessmentRunId { get; set; }
    public required AssessmentPhase CurrentPhase { get; set; }
    public required string TargetScope { get; set; }
    public List<CommandExecutionResult> PreviousResults { get; set; } = new();
    public List<Finding> CurrentFindings { get; set; } = new();
    public List<ToolCapability> AvailableTools { get; set; } = new();
    public Dictionary<string, object> PhaseGoals { get; set; } = new();
    public List<string> CompletedObjectives { get; set; } = new();
    public DateTime PhaseStartedAt { get; set; }
    public int MaxIterations { get; set; } = 10;
    public int CurrentIteration { get; set; }
}

/// <summary>
/// Result of agent planning with next actions to take.
/// </summary>
public class AgentPlanResult
{
    public required bool ContinuePhase { get; set; }
    public required bool AdvancePhase { get; set; }
    public AssessmentPhase? NextPhase { get; set; }
    public List<CommandExecutionRequest> NextCommands { get; set; } = new();
    public List<Finding> ExtractedFindings { get; set; } = new();
    public string? Reasoning { get; set; }
    public Dictionary<string, object> UpdatedGoals { get; set; } = new();
    public List<string> NewObjectives { get; set; } = new();
}

/// <summary>
/// Represents an extracted finding from command output.
/// </summary>
public class Finding
{
    public required string Type { get; set; }
    public required string Severity { get; set; }
    public required string Description { get; set; }
    public required string Target { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public string? Evidence { get; set; }
    public List<string> RelatedCves { get; set; } = new();
}

public enum AssessmentPhase
{
    Recon,
    Enumeration,
    Prioritization,
    Validation,
    Reporting
}
