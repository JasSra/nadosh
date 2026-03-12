namespace Nadosh.Core.Configuration;

public sealed class EdgeControlPlaneOptions
{
    public const string SectionName = "EdgeControlPlane";

    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int HeartbeatIntervalSeconds { get; set; } = 30;
    public bool FetchPendingTasks { get; set; } = true;
    public List<string> Capabilities { get; set; } = new();
}
