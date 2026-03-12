using System;

namespace Nadosh.Core.Models;

public class CurrentExposure
{
    public long Id { get; set; }
    public string TargetId { get; set; } = string.Empty; // IP
    public int Port { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public string CurrentState { get; set; } = string.Empty;
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public DateTime LastChanged { get; set; }
    public string? Classification { get; set; }
    public string? Severity { get; set; }
    public string? CachedSummary { get; set; }
    
    // ── MAC address enrichment fields ──
    public string? MacAddress { get; set; }
    public string? MacVendor { get; set; }
    public string? DeviceType { get; set; }
    
    // ── CVE enrichment fields ──
    public string? CveIds { get; set; } // Comma-separated list of CVE IDs
    public double? HighestCvssScore { get; set; }
    public string? CveSeverity { get; set; } // Highest CVE severity
    public DateTime? CveLastChecked { get; set; }
}
