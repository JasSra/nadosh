namespace Nadosh.Core.Models;

public class AuthorizedTask
{
    public string TaskId { get; set; } = Guid.NewGuid().ToString("n");
    public string SiteId { get; set; } = string.Empty;
    public string? AgentId { get; set; }
    public string TaskKind { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string ScopeJson { get; set; } = "{}";
    public List<string> RequiredCapabilities { get; set; } = new();
    public AuthorizedTaskStatus Status { get; set; } = AuthorizedTaskStatus.Queued;
    public int Priority { get; set; }
    public int AttemptCount { get; set; }
    public string? LeaseToken { get; set; }
    public string? IssuedBy { get; set; }
    public string? ApprovalReference { get; set; }
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? NotBefore { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ResultSummaryJson { get; set; }
}

public enum AuthorizedTaskStatus
{
    Queued = 0,
    Claimed = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}
