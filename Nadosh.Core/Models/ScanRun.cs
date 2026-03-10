using System;

namespace Nadosh.Core.Models;

public class ScanRun
{
    public string RunId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Shard { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string WorkerId { get; set; } = string.Empty;
    public string CountsJson { get; set; } = "{}";
    public string Status { get; set; } = string.Empty;
}
