namespace Nadosh.Core.Models;

public sealed class AssessmentToolDefinition
{
    public string ToolId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public AssessmentToolExecutionMode ExecutionMode { get; init; } = AssessmentToolExecutionMode.Passive;
    public AssessmentToolRiskLevel RiskLevel { get; init; } = AssessmentToolRiskLevel.Low;
    public bool EnabledByDefault { get; init; } = true;
    public bool ExternalUseAllowed { get; init; }
    public bool LabUseAllowed { get; init; } = true;
    public bool RequiresApprovalForExternalTargets { get; init; } = true;
    public bool ProducesEvidence { get; init; } = true;
    public bool AllowsStateChangingActions { get; init; }
    public bool AllowsBinaryPayloads { get; init; }
    public bool AllowsRemoteCodeExecution { get; init; }
    public string[] RequiredTags { get; init; } = [];
    public string[] SafetyChecks { get; init; } = [];
    public IReadOnlyList<AssessmentToolParameterDefinition> Parameters { get; init; } = Array.Empty<AssessmentToolParameterDefinition>();
}

public sealed class AssessmentToolParameterDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = "string";
    public string Description { get; init; } = string.Empty;
    public bool Required { get; init; }
    public string[] AllowedValues { get; init; } = [];
}

public enum AssessmentToolExecutionMode
{
    Passive,
    ActiveValidation,
    DryRunOnly
}

public enum AssessmentToolRiskLevel
{
    Low,
    Moderate,
    High
}
