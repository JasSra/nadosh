namespace Nadosh.Core.Models;

public class Stage1ScanJob
{
    public string BatchId { get; set; } = string.Empty;
    public string TargetIp { get; set; } = string.Empty;
    public List<int> PortsToScan { get; set; } = new();
}

public class ClassificationJob
{
    public Observation Observation { get; set; } = null!;
}

public class Stage2EnrichmentJob
{
    public string ObservationId { get; set; } = string.Empty;
    public string TargetIp { get; set; } = string.Empty;
    public List<string> RuleIds { get; set; } = new();
}

// ── New Tier-based job messages ──

/// <summary>
/// Tier 1: Banner grab job — do a full TCP connect on a confirmed-open port
/// and capture the raw service banner / initial response bytes.
/// </summary>
public class BannerGrabJob
{
    public string BatchId { get; set; } = string.Empty;
    public string TargetIp { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Protocol { get; set; } = "tcp";
    public long DiscoveryObservationId { get; set; }
}

/// <summary>
/// Tier 2: Deep fingerprint job — run protocol-specific probes
/// (HTTP GET, TLS ClientHello, SSH banner read) on a bannered port.
/// </summary>
public class FingerprintJob
{
    public string BatchId { get; set; } = string.Empty;
    public string TargetIp { get; set; } = string.Empty;
    public int Port { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public long BannerObservationId { get; set; }
}
/// <summary>
/// MAC vendor enrichment job — lookup MAC address vendor information
/// from IEEE OUI database and enrich target with device type hints.
/// </summary>
public class MacEnrichmentJob
{
    public string TargetIp { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
}