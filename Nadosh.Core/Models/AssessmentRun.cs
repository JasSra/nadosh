namespace Nadosh.Core.Models;

public class AssessmentRun
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("n");
    public string ToolId { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string TargetScope { get; set; } = string.Empty;
    public AssessmentScopeKind ScopeKind { get; set; } = AssessmentScopeKind.IpAddress;
    public AssessmentExecutionEnvironment Environment { get; set; } = AssessmentExecutionEnvironment.Lab;
    public string ParametersJson { get; set; } = "{}";
    public string PolicyDecisionJson { get; set; } = "{}";
    public string? ApprovalReference { get; set; }
    public string? ResultSummaryJson { get; set; }
    public bool DryRun { get; set; }
    public bool RequiresApproval { get; set; }
    public AssessmentRunStatus Status { get; set; } = AssessmentRunStatus.PendingPolicy;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public enum AssessmentExecutionEnvironment
{
    Lab = 0,
    InternalAuthorized = 1,
    ExternalAuthorized = 2
}

public enum AssessmentScopeKind
{
    IpAddress = 0,
    Cidr = 1,
    Hostname = 2,
    Service = 3,
    Application = 4,
    Workflow = 5
}

public enum AssessmentRunStatus
{
    PendingPolicy = 0,
    AwaitingApproval = 1,
    Queued = 2,
    InProgress = 3,
    Completed = 4,
    Denied = 5,
    Failed = 6,
    Cancelled = 7
}
