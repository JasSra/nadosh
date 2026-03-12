using Nadosh.Core.Models;
using Microsoft.Extensions.Logging;

namespace Nadosh.Core.Services;

/// <summary>
/// ML-based threat scoring service that calculates a risk score (0-100) for network exposures.
/// Combines multiple risk factors: service type, port, CVE severity, CVSS scores, exposure duration,
/// change frequency, geolocation, and state. Higher scores indicate higher risk.
/// </summary>
public class ThreatScoringService
{
    private readonly ILogger<ThreatScoringService> _logger;

    // Weight factors for different risk dimensions (total = 100%)
    private const double ServiceTypeWeight = 0.25;      // 25%
    private const double CveSeverityWeight = 0.30;      // 30%
    private const double ExposureDurationWeight = 0.15; // 15%
    private const double ChangeFrequencyWeight = 0.10;  // 10%
    private const double GeolocationWeight = 0.10;      // 10%
    private const double PortRiskWeight = 0.10;         // 10%

    public ThreatScoringService(ILogger<ThreatScoringService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Calculate comprehensive threat score for an exposure (0-100, higher = more dangerous)
    /// </summary>
    public ThreatScore CalculateThreatScore(CurrentExposure exposure, Target? target = null)
    {
        try
        {
            var components = new ThreatScoreComponents();

            // 1. Service Type Risk (25%)
            components.ServiceTypeScore = CalculateServiceTypeRisk(exposure.Classification, exposure.Port);

            // 2. CVE/Vulnerability Risk (30%)
            components.CveScore = CalculateCveRisk(exposure.HighestCvssScore, exposure.CveSeverity);

            // 3. Exposure Duration Risk (15%)
            components.ExposureDurationScore = CalculateExposureDurationRisk(exposure.FirstSeen);

            // 4. Change Frequency Risk (10%)
            components.ChangeFrequencyScore = CalculateChangeFrequencyRisk(exposure.FirstSeen, exposure.LastChanged);

            // 5. Geolocation Risk (10%)
            components.GeolocationScore = CalculateGeolocationRisk(target?.GeoCountry);

            // 6. Port Risk (10%)
            components.PortScore = CalculatePortRisk(exposure.Port);

            // Weighted total score
            var totalScore = 
                (components.ServiceTypeScore * ServiceTypeWeight) +
                (components.CveScore * CveSeverityWeight) +
                (components.ExposureDurationScore * ExposureDurationWeight) +
                (components.ChangeFrequencyScore * ChangeFrequencyWeight) +
                (components.GeolocationScore * GeolocationWeight) +
                (components.PortScore * PortRiskWeight);

            // State modifier: reduce score if not open
            if (exposure.CurrentState != "open")
            {
                totalScore *= 0.3; // Filtered/closed ports are 70% less risky
            }

            var finalScore = Math.Min(100, Math.Max(0, totalScore));
            var severity = DetermineThreatLevel(finalScore);
            var explanation = GenerateExplanation(components, exposure, finalScore);

            _logger.LogDebug("Calculated threat score {Score} ({Severity}) for {IP}:{Port}", 
                finalScore, severity, exposure.TargetId, exposure.Port);

            return new ThreatScore
            {
                Score = Math.Round(finalScore, 1),
                Severity = severity,
                Explanation = explanation,
                Components = components
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate threat score for {IP}:{Port}", 
                exposure.TargetId, exposure.Port);
            return new ThreatScore { Score = 0, Severity = "unknown", Explanation = "Scoring failed" };
        }
    }

    private double CalculateServiceTypeRisk(string? service, int port)
    {
        // High-risk services (100 points)
        var criticalServices = new[] { "telnet", "ftp", "smb", "rdp", "vnc", "snmp", "mysql", "postgresql", 
                                        "mongodb", "redis", "elasticsearch", "cassandra", "couchdb" };
        if (!string.IsNullOrEmpty(service) && criticalServices.Contains(service.ToLowerInvariant()))
            return 100;

        // Medium-risk services (60 points)
        var mediumServices = new[] { "ssh", "ldap", "smtp", "pop3", "imap", "dns", "ntp" };
        if (!string.IsNullOrEmpty(service) && mediumServices.Contains(service.ToLowerInvariant()))
            return 60;

        // Low-risk services (30 points)
        var lowServices = new[] { "http", "https" };
        if (!string.IsNullOrEmpty(service) && lowServices.Contains(service.ToLowerInvariant()))
            return 30;

        // Unknown service - base on port only
        return port > 1024 ? 20 : 40; // High ports are typically less critical
    }

    private double CalculateCveRisk(double? cvssScore, string? cveSeverity)
    {
        if (cvssScore.HasValue && cvssScore.Value > 0)
        {
            // CVSS 9.0-10.0 = 100 points (critical)
            // CVSS 7.0-8.9 = 80 points (high)
            // CVSS 4.0-6.9 = 50 points (medium)
            // CVSS 0.1-3.9 = 25 points (low)
            return cvssScore.Value switch
            {
                >= 9.0 => 100,
                >= 7.0 => 80,
                >= 4.0 => 50,
                > 0 => 25,
                _ => 0
            };
        }

        // Fallback to severity string if no CVSS score
        if (!string.IsNullOrEmpty(cveSeverity))
        {
            return cveSeverity.ToLowerInvariant() switch
            {
                "critical" => 100,
                "high" => 80,
                "medium" => 50,
                "low" => 25,
                _ => 0
            };
        }

        // No known vulnerabilities
        return 0;
    }

    private double CalculateExposureDurationRisk(DateTime firstSeen)
    {
        var duration = DateTime.UtcNow - firstSeen;

        // Longer exposure = higher risk (more time for discovery/exploitation)
        return duration.TotalDays switch
        {
            >= 365 => 100, // 1+ years exposed
            >= 180 => 80,  // 6+ months
            >= 90 => 60,   // 3+ months
            >= 30 => 40,   // 1+ month
            >= 7 => 20,    // 1+ week
            _ => 10        // Less than 1 week
        };
    }

    private double CalculateChangeFrequencyRisk(DateTime firstSeen, DateTime lastChanged)
    {
        var totalDuration = DateTime.UtcNow - firstSeen;
        var timeSinceChange = DateTime.UtcNow - lastChanged;

        // Recent changes indicate active service (higher risk)
        // No changes indicate stable/potentially forgotten service (medium risk)
        if (timeSinceChange.TotalHours < 24)
            return 100; // Changed in last 24h = very active

        if (timeSinceChange.TotalDays < 7)
            return 70; // Changed in last week = active

        if (timeSinceChange.TotalDays < 30)
            return 40; // Changed in last month = moderately active

        // Old, unchanged service - could be forgotten/unpatched
        return 30;
    }

    private double CalculateGeolocationRisk(string? country)
    {
        if (string.IsNullOrEmpty(country))
            return 50; // Unknown location = medium risk

        // High-risk countries (based on common threat actor origins)
        var highRiskCountries = new[] { "CN", "RU", "KP", "IR", "SY" };
        if (highRiskCountries.Contains(country.ToUpperInvariant()))
            return 100;

        // Medium-risk countries
        var mediumRiskCountries = new[] { "VN", "IN", "PK", "BR", "UA" };
        if (mediumRiskCountries.Contains(country.ToUpperInvariant()))
            return 60;

        // Low-risk countries (trusted regions)
        var lowRiskCountries = new[] { "US", "CA", "GB", "DE", "FR", "AU", "NZ", "JP", "KR", "SG" };
        if (lowRiskCountries.Contains(country.ToUpperInvariant()))
            return 20;

        // Default for other countries
        return 40;
    }

    private double CalculatePortRisk(int port)
    {
        // Well-known vulnerable ports
        var criticalPorts = new[] { 23, 21, 445, 139, 3389, 5900, 161, 1433, 3306, 5432, 6379, 27017, 9200 };
        if (criticalPorts.Contains(port))
            return 100;

        // Commonly targeted ports
        var highRiskPorts = new[] { 22, 25, 110, 143, 389, 636, 1521, 5984, 8080, 8443, 9090 };
        if (highRiskPorts.Contains(port))
            return 60;

        // Standard web ports
        if (port == 80 || port == 443)
            return 30;

        // High ports (typically less critical)
        if (port > 1024)
            return 20;

        // Other well-known ports
        return 40;
    }

    private string DetermineThreatLevel(double score)
    {
        return score switch
        {
            >= 80 => "critical",
            >= 60 => "high",
            >= 40 => "medium",
            >= 20 => "low",
            _ => "minimal"
        };
    }

    private string GenerateExplanation(ThreatScoreComponents components, CurrentExposure exposure, double finalScore)
    {
        var factors = new List<string>();

        // Service type contribution
        if (components.ServiceTypeScore >= 80)
            factors.Add($"high-risk service ({exposure.Classification})");
        else if (components.ServiceTypeScore >= 50)
            factors.Add($"moderate-risk service ({exposure.Classification})");

        // CVE contribution
        if (components.CveScore >= 80)
            factors.Add($"critical vulnerabilities (CVSS {exposure.HighestCvssScore:F1})");
        else if (components.CveScore >= 50)
            factors.Add("known vulnerabilities");

        // Duration contribution
        if (components.ExposureDurationScore >= 60)
            factors.Add("long-term exposure");

        // Change frequency
        if (components.ChangeFrequencyScore >= 70)
            factors.Add("recently modified");

        // Port risk
        if (components.PortScore >= 80)
            factors.Add($"high-risk port {exposure.Port}");

        // State modifier
        if (exposure.CurrentState != "open")
            factors.Add($"status: {exposure.CurrentState}");

        var explanation = factors.Any() 
            ? $"Threat factors: {string.Join(", ", factors)}" 
            : "Low risk profile";

        return explanation;
    }
}

/// <summary>
/// Threat score result with severity classification and explanation
/// </summary>
public class ThreatScore
{
    public double Score { get; set; }
    public string Severity { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public ThreatScoreComponents? Components { get; set; }
}

/// <summary>
/// Individual component scores that contribute to the overall threat score
/// </summary>
public class ThreatScoreComponents
{
    public double ServiceTypeScore { get; set; }
    public double CveScore { get; set; }
    public double ExposureDurationScore { get; set; }
    public double ChangeFrequencyScore { get; set; }
    public double GeolocationScore { get; set; }
    public double PortScore { get; set; }
}
