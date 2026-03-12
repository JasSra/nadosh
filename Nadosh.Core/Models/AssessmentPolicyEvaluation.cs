namespace Nadosh.Core.Models;

public sealed class AssessmentPolicyEvaluationRequest
{
    public string ToolId { get; init; } = string.Empty;
    public string RequestedBy { get; init; } = string.Empty;
    public string TargetScope { get; init; } = string.Empty;
    public AssessmentScopeKind ScopeKind { get; init; } = AssessmentScopeKind.IpAddress;
    public AssessmentExecutionEnvironment Environment { get; init; } = AssessmentExecutionEnvironment.Lab;
    public string? ApprovalReference { get; init; }
    public bool DryRun { get; init; }
    public IReadOnlyCollection<string> ScopeTags { get; init; } = Array.Empty<string>();
}

public sealed class AssessmentPolicyEvaluation
{
    public AssessmentPolicyDecision Decision { get; init; } = AssessmentPolicyDecision.Denied;
    public bool IsAllowed { get; init; }
    public bool RequiresApproval { get; init; }
    public string ToolId { get; init; } = string.Empty;
    public string[] Reasons { get; init; } = [];
    public string[] MissingRequirements { get; init; } = [];
    public string[] AppliedSafetyChecks { get; init; } = [];
}

public enum AssessmentPolicyDecision
{
    Allowed = 0,
    RequiresApproval = 1,
    Denied = 2
}
