using Microsoft.Extensions.Logging;
using Moq;
using Nadosh.Core.Models;
using Nadosh.Core.Services;
using Xunit;

namespace Nadosh.Core.Tests.Services;

public class ThreatScoringServiceTests
{
    private readonly ThreatScoringService _service;
    private readonly Mock<ILogger<ThreatScoringService>> _loggerMock;

    public ThreatScoringServiceTests()
    {
        _loggerMock = new Mock<ILogger<ThreatScoringService>>();
        _service = new ThreatScoringService(_loggerMock.Object);
    }

    [Fact]
    public void CalculateThreatScore_CriticalServiceWithHighCVE_ReturnsHighScore()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 3389,
            Protocol = "TCP",
            Classification = "rdp",
            CurrentState = "open",
            FirstSeen = DateTime.UtcNow.AddYears(-1),
            LastSeen = DateTime.UtcNow,
            LastChanged = DateTime.UtcNow.AddHours(-1),
            HighestCvssScore = 9.8,
            CveSeverity = "critical",
            CveIds = "CVE-2019-0708,CVE-2020-0610"
        };

        var target = new Target
        {
            Ip = "192.168.1.100",
            Country = "CN"
        };

        // Act
        var result = _service.CalculateThreatScore(exposure, target);

        // Assert
        Assert.True(result.Score >= 80, $"Expected critical threat score >= 80, got {result.Score}");
        Assert.Equal("critical", result.Severity);
        Assert.NotNull(result.Explanation);
        Assert.Contains("RDP", result.Explanation);
        Assert.Contains("critical", result.Explanation.ToLower());
    }

    [Fact]
    public void CalculateThreatScore_SSHWithNoCVE_ReturnsMediumScore()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 22,
            Protocol = "TCP",
            Classification = "ssh",
            CurrentState = "open",
            FirstSeen = DateTime.UtcNow.AddMonths(-3),
            LastSeen = DateTime.UtcNow,
            LastChanged = DateTime.UtcNow.AddDays(-7),
            HighestCvssScore = null,
            CveSeverity = null
        };

        var target = new Target
        {
            Ip = "10.0.1.50",
            Country = "US"
        };

        // Act
        var result = _service.CalculateThreatScore(exposure, target);

        // Assert
        Assert.True(result.Score >= 30 && result.Score < 80, $"Expected medium score, got {result.Score}");
        Assert.True(new[] { "low", "medium", "high" }.Contains(result.Severity));
        Assert.NotNull(result.Components);
    }

    [Fact]
    public void CalculateThreatScore_ClosedPort_ReturnsReducedScore()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 3389,
            Protocol = "TCP",
            Classification = "rdp",
            CurrentState = "closed",
            FirstSeen = DateTime.UtcNow.AddDays(-1),
            LastSeen = DateTime.UtcNow,
            LastChanged = DateTime.UtcNow,
            HighestCvssScore = 9.0,
            CveSeverity = "critical"
        };

        var target = new Target { Ip = "192.168.1.1" };

        // Act
        var result = _service.CalculateThreatScore(exposure, target);

        // Assert - Closed ports should have 70% reduction
        Assert.True(result.Score < 30, $"Closed port should have reduced score, got {result.Score}");
    }

    [Fact]
    public void CalculateThreatScore_DatabaseService_ReturnsHighServiceTypeScore()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 3306,
            Protocol = "TCP",
            Classification = "mysql",
            CurrentState = "open",
            FirstSeen = DateTime.UtcNow.AddDays(-30),
            LastSeen = DateTime.UtcNow
        };

        var target = new Target { Ip = "10.0.1.100" };

        // Act
        var result = _service.CalculateThreatScore(exposure, target);

        // Assert
        Assert.NotNull(result.Components);
        Assert.Equal(100, result.Components.ServiceTypeScore); // Database should be critical (100)
    }

    [Fact]
    public void CalculateThreatScore_LongExposure_ReturnsHighDurationScore()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 80,
            Protocol = "TCP",
            Classification = "http",
            CurrentState = "open",
            FirstSeen = DateTime.UtcNow.AddYears(-2),
            LastSeen = DateTime.UtcNow
        };

        var target = new Target { Ip = "192.168.1.1" };

        // Act
        var result = _service.CalculateThreatScore(exposure, target);

        // Assert
        Assert.NotNull(result.Components);
        Assert.Equal(100, result.Components.ExposureDurationScore); // 2 years = 100
    }

    [Fact]
    public void CalculateThreatScore_RecentChange_ReturnsHighChangeFrequencyScore()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 443,
            Protocol = "TCP",
            Classification = "https",
            CurrentState = "open",
            FirstSeen = DateTime.UtcNow.AddMonths(-6),
            LastSeen = DateTime.UtcNow,
            LastChanged = DateTime.UtcNow.AddHours(-12)
        };

        var target = new Target { Ip = "192.168.1.1" };

        // Act
        var result = _service.CalculateThreatScore(exposure, target);

        // Assert
        Assert.NotNull(result.Components);
        Assert.Equal(100, result.Components.ChangeFrequencyScore); // < 24h = 100
    }

    [Fact]
    public void CalculateThreatScore_HighRiskCountry_ReturnsHighGeoScore()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 22,
            Protocol = "TCP",
            Classification = "ssh",
            CurrentState = "open",
            FirstSeen = DateTime.UtcNow.AddDays(-30),
            LastSeen = DateTime.UtcNow
        };

        var target = new Target
        {
            Ip = "192.168.1.1",
            Country = "RU" // High-risk country
        };

        // Act
        var result = _service.CalculateThreatScore(exposure, target);

        // Assert
        Assert.NotNull(result.Components);
        Assert.Equal(100, result.Components.GeolocationScore);
    }

    [Fact]
    public void CalculateThreatScore_DangerousPort_ReturnsHighPortRiskScore()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 23, // Telnet - dangerous
            Protocol = "TCP",
            Classification = "telnet",
            CurrentState = "open",
            FirstSeen = DateTime.UtcNow.AddDays(-10),
            LastSeen = DateTime.UtcNow
        };

        var target = new Target { Ip = "192.168.1.1" };

        // Act
        var result = _service.CalculateThreatScore(exposure, target);

        // Assert
        Assert.NotNull(result.Components);
        Assert.Equal(100, result.Components.PortScore);
    }

    [Fact]
    public void CalculateThreatScore_CVSSScores_CorrectSeverityMapping()
    {
        // Test various CVSS scores map to correct severity
        var testCases = new[]
        {
            (cvss: 10.0, expectedMin: 90.0, severity: "critical"),
            (cvss: 8.5, expectedMin: 60.0, severity: "high"),
            (cvss: 5.0, expectedMin: 30.0, severity: "medium"),
            (cvss: 2.0, expectedMin: 0.0, severity: "low")
        };

        foreach (var (cvss, expectedMin, severity) in testCases)
        {
            var exposure = new CurrentExposure
            {
                Port = 80,
                Protocol = "TCP",
                Classification = "http",
                CurrentState = "open",
                FirstSeen = DateTime.UtcNow.AddDays(-30),
                LastSeen = DateTime.UtcNow,
                HighestCvssScore = cvss
            };

            var result = _service.CalculateThreatScore(exposure, null);
            
            Assert.True(result.Score >= expectedMin, 
                $"CVSS {cvss} should produce score >= {expectedMin}, got {result.Score}");
        }
    }

    [Fact]
    public void CalculateThreatScore_NullExposure_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            _service.CalculateThreatScore(null!, null));
    }

    [Fact]
    public void CalculateThreatScore_ComponentsAddUpCorrectly()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 3389,
            Protocol = "TCP",
            Classification = "rdp",
            CurrentState = "open",
            FirstSeen = DateTime.UtcNow.AddYears(-1),
            LastSeen = DateTime.UtcNow,
            LastChanged = DateTime.UtcNow.AddHours(-1),
            HighestCvssScore = 9.8
        };

        var target = new Target
        {
            Ip = "192.168.1.100",
            Country = "CN"
        };

        // Act
        var result = _service.CalculateThreatScore(exposure, target);

        // Assert - verify components exist and contribute to final score
        Assert.NotNull(result.Components);
        
        var calculatedScore = 
            result.Components.ServiceTypeScore * 0.25 +
            result.Components.CveScore * 0.30 +
            result.Components.ExposureDurationScore * 0.15 +
            result.Components.ChangeFrequencyScore * 0.10 +
            result.Components.GeolocationScore * 0.10 +
            result.Components.PortScore * 0.10;

        Assert.Equal(result.Score, calculatedScore, 0.1); // Allow small rounding difference
    }

    [Fact]
    public void CalculateThreatScore_MinimalThreat_ReturnsMinimalSeverity()
    {
        // Arrange - safe service, no CVEs, recent exposure, low-risk location
        var exposure = new CurrentExposure
        {
            Port = 8080,
            Protocol = "TCP",
            Classification = "http",
            CurrentState = "open",
            FirstSeen = DateTime.UtcNow.AddDays(-5),
            LastSeen = DateTime.UtcNow,
            LastChanged = DateTime.UtcNow.AddMonths(-2),
            HighestCvssScore = null
        };

        var target = new Target
        {
            Ip = "192.168.1.100",
            Country = "US"
        };

        // Act
        var result = _service.CalculateThreatScore(exposure, target);

        // Assert
        Assert.True(result.Score < 40, $"Expected low score for minimal threat, got {result.Score}");
        Assert.True(new[] { "minimal", "low", "medium" }.Contains(result.Severity));
    }
}
