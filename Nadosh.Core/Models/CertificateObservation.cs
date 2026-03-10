using System;
using System.Collections.Generic;

namespace Nadosh.Core.Models;

public class CertificateObservation
{
    public long Id { get; set; }
    public string TargetId { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public List<string> SanList { get; set; } = new();
    public string Issuer { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public DateTime ObservedAt { get; set; }

    // ── Extended TLS fingerprinting fields ──
    public string? SerialNumber { get; set; }
    public string? SignatureAlgorithm { get; set; }
    public int? KeySize { get; set; }
    public bool IsExpired { get; set; }
    public int DaysUntilExpiry { get; set; }
    public bool IsSelfSigned { get; set; }
}
