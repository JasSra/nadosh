namespace Nadosh.Core.Models;

public sealed class AssessmentRunSubmissionRequest
{
    public string ToolId { get; init; } = string.Empty;
    public string RequestedBy { get; init; } = string.Empty;
    public string TargetScope { get; init; } = string.Empty;
    public AssessmentScopeKind ScopeKind { get; init; } = AssessmentScopeKind.IpAddress;
    public AssessmentExecutionEnvironment Environment { get; init; } = AssessmentExecutionEnvironment.Lab;
    public string ParametersJson { get; init; } = "{}";
    public string? ApprovalReference { get; init; }
    public bool DryRun { get; init; }
    public IReadOnlyCollection<string> ScopeTags { get; init; } = Array.Empty<string>();
}

public sealed class AssessmentRunSubmissionResult
{
    public AssessmentRun Run { get; init; } = new();
    public AssessmentPolicyEvaluation PolicyEvaluation { get; init; } = new();
}
