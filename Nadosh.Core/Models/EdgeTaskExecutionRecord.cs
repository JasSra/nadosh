namespace Nadosh.Core.Models;

public class EdgeTaskExecutionRecord
{
    public long Id { get; set; }
    public string AuthorizedTaskId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string TaskKind { get; set; } = string.Empty;
    public string LeaseToken { get; set; } = string.Empty;
    public string? LocalQueueName { get; set; }
    public string? LocalJobReference { get; set; }
    public EdgeTaskExecutionStatus Status { get; set; } = EdgeTaskExecutionStatus.QueuedLocal;
    public string? Summary { get; set; }
    public string? MetadataJson { get; set; }
    public bool RequeueRecommended { get; set; }
    public int UploadAttemptCount { get; set; }
    public string? LastUploadError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? UploadedAt { get; set; }
    public DateTime? NextUploadAttemptAt { get; set; }
}

public enum EdgeTaskExecutionStatus
{
    QueuedLocal = 0,
    CompletedLocal = 1,
    FailedLocal = 2,
    Uploaded = 3
}
