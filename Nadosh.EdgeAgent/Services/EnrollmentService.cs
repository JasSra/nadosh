using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nadosh.EdgeAgent.Services;

public class EnrollmentService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AgentConfiguration _config;
    private readonly ILogger<EnrollmentService> _logger;

    public EnrollmentService(
        IHttpClientFactory httpClientFactory,
        AgentConfiguration config,
        ILogger<EnrollmentService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<bool> EnrollAsync()
    {
        try
        {
            _logger.LogInformation("Enrolling with mothership...");

            var client = _httpClientFactory.CreateClient("Mothership");
            
            var enrollmentData = new
            {
                siteId = _config.SiteId,
                agentId = _config.AgentId,
                hostname = _config.Hostname,
                platform = _config.Platform,
                version = _config.Version,
                workerRoles = _config.WorkerRoles,
                capabilities = GetCapabilities()
            };

            var response = await client.PostAsJsonAsync("/api/edge/agents/enroll", enrollmentData);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                _logger.LogInformation("✓ Successfully enrolled with mothership");
                _logger.LogInformation("  Status: {Status}", result.GetProperty("status").GetString());
                return true;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("✗ Enrollment failed: {Status} - {Error}", response.StatusCode, error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "✗ Enrollment exception: {Message}", ex.Message);
            return false;
        }
    }

    private Dictionary<string, object> GetCapabilities()
    {
        var caps = new Dictionary<string, object>
        {
            ["networkScanning"] = true,
            ["portScanning"] = true,
            ["serviceDetection"] = true,
            ["vulnerabilityScanning"] = false, // Requires additional tools
            ["compliance"] = false
        };

        // Windows-specific capabilities
        if (OperatingSystem.IsWindows())
        {
            caps["windowsServices"] = true;
            caps["registryMonitoring"] = true;
            caps["eventLogCollection"] = true;
        }

        // Linux-specific capabilities
        if (OperatingSystem.IsLinux())
        {
            caps["systemdServices"] = true;
            caps["packageManagement"] = true;
            caps["iptablesMonitoring"] = true;
        }

        return caps;
    }
}
