namespace Nadosh.Core.Models;

public class EdgeAgent
{
    public string AgentId { get; set; } = Guid.NewGuid().ToString("n");
    public string SiteId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
    public EdgeAgentStatus Status { get; set; } = EdgeAgentStatus.Pending;
    public List<string> AdvertisedCapabilities { get; set; } = new();
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAt { get; set; }
    public DateTime? LastHeartbeatAt { get; set; }
    public string? LastKnownAddress { get; set; }
    public string? MetadataJson { get; set; }
}

public enum EdgeAgentStatus
{
    Pending = 0,
    Active = 1,
    Disabled = 2
}
