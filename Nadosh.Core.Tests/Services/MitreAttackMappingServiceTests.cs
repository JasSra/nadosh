using Nadosh.Core.Models;
using Nadosh.Core.Services;
using Xunit;

namespace Nadosh.Core.Tests.Services;

public class MitreAttackMappingServiceTests
{
    private readonly MitreAttackMappingService _service;

    public MitreAttackMappingServiceTests()
    {
        _service = new MitreAttackMappingService();
    }

    [Fact]
    public void MapExposureToMitre_SSHService_MapsToCorrectTechniques()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 22,
            Protocol = "TCP",
            Classification = "ssh",
            CurrentState = "open"
        };

        // Act
        var result = _service.MapExposureToMitre(exposure);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Initial Access", result.Tactics);
        Assert.Contains("Lateral Movement", result.Tactics);
        Assert.Contains("T1021.004", result.Techniques.Keys); // SSH technique
    }

    [Fact]
    public void MapExposureToMitre_RDPService_MapsToCorrectTechniques()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 3389,
            Protocol = "TCP",
            Classification = "rdp",
            CurrentState = "open"
        };

        // Act
        var result = _service.MapExposureToMitre(exposure);

        // Assert
        Assert.Contains("Initial Access", result.Tactics);
        Assert.Contains("Lateral Movement", result.Tactics);
        Assert.Contains("T1021.001", result.Techniques.Keys); // RDP technique
        Assert.Equal("Remote Desktop Protocol", result.Techniques["T1021.001"]);
    }

    [Fact]
    public void MapExposureToMitre_SMBService_MapsToCorrectTechniques()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 445,
            Protocol = "TCP",
            Classification = "smb",
            CurrentState = "open"
        };

        // Act
        var result = _service.MapExposureToMitre(exposure);

        // Assert
        Assert.Contains("Lateral Movement", result.Tactics);
        Assert.Contains("T1021.002", result.Techniques.Keys); // SMB technique
    }

    [Fact]
    public void MapExposureToMitre_DatabaseService_MapsToDataCollection()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 3306,
            Protocol = "TCP",
            Classification = "mysql",
            CurrentState = "open"
        };

        // Act
        var result = _service.MapExposureToMitre(exposure);

        // Assert
        Assert.Contains("Collection", result.Tactics);
        Assert.Contains("Impact", result.Tactics);
        Assert.Contains("T1213", result.Techniques.Keys); // Data from Information Repositories
    }

    [Fact]
    public void MapExposureToMitre_HTTPService_MapsToWebExploitation()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 80,
            Protocol = "TCP",
            Classification = "http",
            CurrentState = "open"
        };

        // Act
        var result = _service.MapExposureToMitre(exposure);

        // Assert
        Assert.Contains("Initial Access", result.Tactics);
        Assert.Contains("T1190", result.Techniques.Keys); // Exploit Public-Facing Application
    }

    [Fact]
    public void MapExposureToMitre_WithCVE_AddsExploitationTechniques()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 22,
            Protocol = "TCP",
            Classification = "ssh",
            CurrentState = "open",
            CveIds = "CVE-2023-1234",
            HighestCvssScore = 9.8
        };

        // Act
        var result = _service.MapExposureToMitre(exposure);

        // Assert
        Assert.Contains("T1190", result.Techniques.Keys); // Exploit Public-Facing Application
    }

    [Fact]
    public void MapExposureToMitre_TelnetService_MapsToInsecureProtocols()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 23,
            Protocol = "TCP",
            Classification = "telnet",
            CurrentState = "open"
        };

        // Act
        var result = _service.MapExposureToMitre(exposure);

        // Assert
        Assert.Contains("Initial Access", result.Tactics);
        Assert.Contains("Credential Access", result.Tactics);
        Assert.NotEmpty(result.Techniques);
    }

    [Fact]
    public void MapExposureToMitre_FTPService_MapsToDataExfiltration()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 21,
            Protocol = "TCP",
            Classification = "ftp",
            CurrentState = "open"
        };

        // Act
        var result = _service.MapExposureToMitre(exposure);

        // Assert
        Assert.Contains("Initial Access", result.Tactics);
        Assert.Contains("Exfiltration", result.Tactics);
        Assert.NotEmpty(result.Techniques);
    }

    [Fact]
    public void GetTacticsString_ReturnsCommaSeparatedString()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 22,
            Protocol = "TCP",
            Classification = "ssh",
            CurrentState = "open"
        };

        var result = _service.MapExposureToMitre(exposure);

        // Act
        var tacticsString = result.GetTacticsString();

        // Assert
        Assert.NotEmpty(tacticsString);
        Assert.Contains(",", tacticsString);
        Assert.Contains("Initial Access", tacticsString);
    }

    [Fact]
    public void GetTechniquesString_ReturnsCommaSeparatedTechniqueIds()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 22,
            Protocol = "TCP",
            Classification = "ssh",
            CurrentState = "open"
        };

        var result = _service.MapExposureToMitre(exposure);

        // Act
        var techniquesString = result.GetTechniquesString();

        // Assert
        Assert.NotEmpty(techniquesString);
        Assert.StartsWith("T", techniquesString);
        Assert.Contains("T1021.004", techniquesString);
    }

    [Fact]
    public void MapExposureToMitre_UnknownService_ReturnsBasicMapping()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 9999,
            Protocol = "TCP",
            Classification = "unknown",
            CurrentState = "open"
        };

        // Act
        var result = _service.MapExposureToMitre(exposure);

        // Assert
        Assert.NotNull(result);
        // Should still have some basic tactics like Discovery
        Assert.Contains("Discovery", result.Tactics);
    }

    [Fact]
    public void MapExposureToMitre_NullExposure_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            _service.MapExposureToMitre(null!));
    }

    [Theory]
    [InlineData("postgresql", 5432)]
    [InlineData("mongodb", 27017)]
    [InlineData("redis", 6379)]
    [InlineData("mssql", 1433)]
    public void MapExposureToMitre_DatabaseServices_AllMapToDataCollection(string service, int port)
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = port,
            Protocol = "TCP",
            Classification = service,
            CurrentState = "open"
        };

        // Act
        var result = _service.MapExposureToMitre(exposure);

        // Assert
        Assert.Contains("Collection", result.Tactics);
        Assert.Contains("T1213", result.Techniques.Keys);
    }

    [Fact]
    public void MapExposureToMitre_LDAPService_MapsToCredentialAccess()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 389,
            Protocol = "TCP",
            Classification = "ldap",
            CurrentState = "open"
        };

        // Act
        var result = _service.MapExposureToMitre(exposure);

        // Assert
        Assert.Contains("Credential Access", result.Tactics);
        Assert.NotEmpty(result.Techniques);
    }

    [Fact]
    public void MapExposureToMitre_DNSService_MapsToCommandAndControl()
    {
        // Arrange
        var exposure = new CurrentExposure
        {
            Port = 53,
            Protocol = "UDP",
            Classification = "dns",
            CurrentState = "open"
        };

        // Act
        var result = _service.MapExposureToMitre(exposure);

        // Assert
        Assert.Contains("Command and Control", result.Tactics);
        Assert.NotEmpty(result.Techniques);
    }

    [Fact]
    public void MapExposureToMitre_MultipleServices_ProducesDistinctTechniques()
    {
        // Arrange
        var ssh = new CurrentExposure { Port = 22, Protocol = "TCP", Classification = "ssh", CurrentState = "open" };
        var rdp = new CurrentExposure { Port = 3389, Protocol = "TCP", Classification = "rdp", CurrentState = "open" };

        // Act
        var sshResult = _service.MapExposureToMitre(ssh);
        var rdpResult = _service.MapExposureToMitre(rdp);

        // Assert
        Assert.NotEqual(sshResult.GetTechniquesString(), rdpResult.GetTechniquesString());
        Assert.Contains("T1021.004", sshResult.Techniques.Keys); // SSH
        Assert.Contains("T1021.001", rdpResult.Techniques.Keys); // RDP
    }
}
