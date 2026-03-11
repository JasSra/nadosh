using System;

namespace Nadosh.Core.Models;

public class Observation
{
    public long Id { get; set; }
    public string TargetId { get; set; } = string.Empty;
    public DateTime ObservedAt { get; set; }
    public int Port { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int? LatencyMs { get; set; }
    public string? Fingerprint { get; set; }
    public string? EvidenceJson { get; set; } // JSONB in DB
    public string? ScanRunId { get; set; }
    public ObservationPipelineState? PipelineState { get; set; }
    public DateTime? PipelineStateChangedAt { get; set; }
    public string? PipelineWorkerId { get; set; }
    public int PipelineRetryCount { get; set; }

    // ── Banner & Fingerprinting fields ──
    public string? Banner { get; set; }
    public string? ServiceName { get; set; }
    public string? ServiceVersion { get; set; }
    public string? ProductVendor { get; set; }
    public string? HttpTitle { get; set; }
    public string? HttpServer { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? SslSubject { get; set; }
    public string? SslIssuer { get; set; }
    public DateTime? SslExpiry { get; set; }
    public string? JarmHash { get; set; }
    public ScanTier Tier { get; set; } = ScanTier.Discovery;
    
    // ── MAC address enrichment fields ──
    public string? MacAddress { get; set; }
    public string? MacVendor { get; set; }
    public string? DeviceType { get; set; }
}

/// <summary>
/// Tracks which scanning tier produced a given observation.
/// </summary>
public enum ScanTier
{
    /// <summary>Tier 0: Fast async port probe — open/closed/filtered</summary>
    Discovery = 0,
    /// <summary>Tier 1: Full TCP connect + raw banner capture</summary>
    BannerGrab = 1,
    /// <summary>Tier 2: Protocol-specific deep fingerprint (TLS, HTTP, SSH)</summary>
    Fingerprint = 2
}
