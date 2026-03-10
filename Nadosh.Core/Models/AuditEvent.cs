using System;

namespace Nadosh.Core.Models;

public class AuditEvent
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? OldValueJson { get; set; }
    public string? NewValueJson { get; set; }
    public string? MetadataJson { get; set; }
}
