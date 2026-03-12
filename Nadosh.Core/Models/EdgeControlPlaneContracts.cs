namespace Nadosh.Core.Models;

public sealed record EdgeEnrollmentRequest
{
    public string SiteId { get; init; } = string.Empty;
    public string SiteName { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Hostname { get; init; } = string.Empty;
    public string OperatingSystem { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public string AgentVersion { get; init; } = string.Empty;
    public IReadOnlyCollection<string> Capabilities { get; init; } = Array.Empty<string>();
    public string? MetadataJson { get; init; }
}

public sealed record EdgeEnrollmentResponse
{
    public string AgentId { get; init; } = string.Empty;
    public string SiteId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int HeartbeatIntervalSeconds { get; init; }
    public DateTime ServerTimeUtc { get; init; }
}

public sealed record EdgeHeartbeatRequest
{
    public string AgentId { get; init; } = string.Empty;
    public string SiteId { get; init; } = string.Empty;
    public string Hostname { get; init; } = string.Empty;
    public string OperatingSystem { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public string AgentVersion { get; init; } = string.Empty;
    public IReadOnlyCollection<string> Capabilities { get; init; } = Array.Empty<string>();
    public string? StatusSummaryJson { get; init; }
}

public sealed record EdgeHeartbeatResponse
{
    public string AgentId { get; init; } = string.Empty;
    public int HeartbeatIntervalSeconds { get; init; }
    public int PendingTaskCount { get; init; }
    public DateTime ServerTimeUtc { get; init; }
}

public sealed record AuthorizedTaskDescriptor
{
    public string TaskId { get; init; } = string.Empty;
    public string SiteId { get; init; } = string.Empty;
    public string? AgentId { get; init; }
    public string TaskKind { get; init; } = string.Empty;
    public string PayloadJson { get; init; } = "{}";
    public string ScopeJson { get; init; } = "{}";
    public IReadOnlyCollection<string> RequiredCapabilities { get; init; } = Array.Empty<string>();
    public int Priority { get; init; }
    public DateTime IssuedAt { get; init; }
    public DateTime? NotBefore { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string? ApprovalReference { get; init; }
}
