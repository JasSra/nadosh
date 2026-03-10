using System;
using System.Collections.Generic;

namespace Nadosh.Core.Models;

public class Target
{
    public string Ip { get; set; } = string.Empty;
    public string? CidrSource { get; set; }
    public List<string> OwnershipTags { get; set; } = new();
    public bool Monitored { get; set; }
    public DateTime? LastScheduled { get; set; }
    public DateTime? NextScheduled { get; set; }

    // ── Adaptive scheduling fields ──
    public int OpenPortCount { get; set; }
    public int StateChangeCount { get; set; }
    public double InterestScore { get; set; }
    public ScanCadence Cadence { get; set; } = ScanCadence.Standard;
    public DateTime? LastStateChange { get; set; }
    public string? ReverseDns { get; set; }
    
    // ── Enrichment fields (ASN, Geolocation) ──
    public string? AsnInfo { get; set; }
    public string? GeoCountry { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? AsnNumber { get; set; }
    public string? AsnOrganization { get; set; }
    public string? IspName { get; set; }
    public string? DataCenter { get; set; }
    public DateTime? EnrichmentCompletedAt { get; set; }
    
    // ── MAC address enrichment fields ──
    public string? MacAddress { get; set; }
    public string? MacVendor { get; set; }
    public string? DeviceType { get; set; }
    public DateTime? MacEnrichmentCompletedAt { get; set; }
}

/// <summary>
/// Controls how frequently a target is re-scanned based on its interest level.
/// </summary>
public enum ScanCadence
{
    /// <summary>Dead IPs — rescan every 30-90 days</summary>
    Cold = 0,
    /// <summary>Default — rescan every 7-15 days</summary>
    Standard = 1,
    /// <summary>Has open ports — rescan every 24 hours</summary>
    Warm = 2,
    /// <summary>Recent state change or high interest — rescan every 6 hours</summary>
    Hot = 3,
    /// <summary>AI-flagged critical — rescan every 1 hour</summary>
    Critical = 4
}
