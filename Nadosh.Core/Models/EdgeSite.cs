namespace Nadosh.Core.Models;

public class EdgeSite
{
    public string SiteId { get; set; } = Guid.NewGuid().ToString("n");
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ApprovalScopeJson { get; set; } = "{}";
    public string DataHandlingPolicyJson { get; set; } = "{}";
    public List<string> AllowedCidrs { get; set; } = new();
    public List<string> AllowedCapabilities { get; set; } = new();
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
