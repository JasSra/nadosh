namespace Nadosh.Core.Models;

public sealed class AssessmentEvidenceBundle
{
    public AssessmentRun Run { get; init; } = new();
    public Target? Target { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public IReadOnlyCollection<CurrentExposure> Exposures { get; init; } = Array.Empty<CurrentExposure>();
    public IReadOnlyCollection<Observation> Observations { get; init; } = Array.Empty<Observation>();
    public IReadOnlyCollection<EnrichmentResult> Enrichments { get; init; } = Array.Empty<EnrichmentResult>();
    public IReadOnlyCollection<string> Notes { get; init; } = Array.Empty<string>();
    public int ExposureCount => Exposures.Count;
    public int ObservationCount => Observations.Count;
    public int EnrichmentCount => Enrichments.Count;
}
