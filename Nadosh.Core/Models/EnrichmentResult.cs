using System;

namespace Nadosh.Core.Models;

public class EnrichmentResult
{
    public long Id { get; set; }
    public long? ObservationId { get; set; }
    public long? CurrentExposureId { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public string RuleVersion { get; set; } = string.Empty;
    public string ResultStatus { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string? Summary { get; set; }
    public string? EvidenceJson { get; set; }
    public DateTime ExecutedAt { get; set; }
}
