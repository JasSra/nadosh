namespace Nadosh.Core.Models;

public sealed class MicrosoftAgentAssessmentContext
{
    public string AdapterName { get; init; } = "Microsoft Agent Framework";
    public string AdapterVersion { get; init; } = "preview-bridge-v1";
    public string SessionId { get; init; } = string.Empty;
    public AssessmentRun Run { get; init; } = new();
    public AssessmentEvidenceBundle? EvidenceBundle { get; init; }
    public string SystemInstructions { get; init; } = string.Empty;
    public IReadOnlyCollection<string> WorkflowHints { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<MicrosoftAgentToolManifest> Tools { get; init; } = Array.Empty<MicrosoftAgentToolManifest>();
}

public sealed class MicrosoftAgentToolManifest
{
    public string ToolId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public AssessmentToolExecutionMode ExecutionMode { get; init; }
    public AssessmentToolRiskLevel RiskLevel { get; init; }
    public bool IsPrimaryTool { get; init; }
    public bool CanExecuteNow { get; init; }
    public MicrosoftAgentToolAvailability Availability { get; init; } = MicrosoftAgentToolAvailability.Disabled;
    public string[] SafetyChecks { get; init; } = [];
    public string[] RequiredTags { get; init; } = [];
    public string[] BlockedReasons { get; init; } = [];
    public IReadOnlyList<AssessmentToolParameterDefinition> Parameters { get; init; } = Array.Empty<AssessmentToolParameterDefinition>();
}

public enum MicrosoftAgentToolAvailability
{
    Ready = 0,
    ApprovalRequired = 1,
    EnvironmentBlocked = 2,
    ExecutionAdapterPlanned = 3,
    Disabled = 4
}
