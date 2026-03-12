using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Nadosh.Core.Configuration;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Workers.Edge;

public sealed class EdgeControlPlaneSyncService : BackgroundService
{
    private static readonly string[] FullCapabilityCatalog =
    [
        "role:scheduler",
        "role:discovery",
        "role:banner",
        "role:fingerprint",
        "role:classifier",
        "role:cache-projector",
        "role:geo-enrichment",
        "role:change-detector",
        "role:mac-enrichment",
        "role:cve-enrichment",
        "role:enrichment"
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAssessmentToolCatalog _assessmentToolCatalog;
    private readonly IOptionsMonitor<EdgeControlPlaneOptions> _optionsMonitor;
    private readonly ILogger<EdgeControlPlaneSyncService> _logger;

    public EdgeControlPlaneSyncService(
        IHttpClientFactory httpClientFactory,
        IAssessmentToolCatalog assessmentToolCatalog,
        IOptionsMonitor<EdgeControlPlaneOptions> optionsMonitor,
        ILogger<EdgeControlPlaneSyncService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _assessmentToolCatalog = assessmentToolCatalog;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            _logger.LogInformation("Edge control-plane sync is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            _logger.LogWarning("Edge control-plane sync is enabled but BaseUrl is missing.");
            return;
        }

        using var httpClient = _httpClientFactory.CreateClient();
        httpClient.BaseAddress = BuildBaseUri(options.BaseUrl);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            httpClient.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
        }

        var enrollment = await TryEnrollAsync(httpClient, options, stoppingToken);
        var heartbeatIntervalSeconds = enrollment?.HeartbeatIntervalSeconds ?? Math.Clamp(options.HeartbeatIntervalSeconds, 10, 300);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(heartbeatIntervalSeconds), stoppingToken);

            var heartbeatResponse = await TryHeartbeatAsync(httpClient, options, stoppingToken);
            if (heartbeatResponse is not null)
            {
                heartbeatIntervalSeconds = heartbeatResponse.HeartbeatIntervalSeconds;
            }

            if (options.FetchPendingTasks)
            {
                await TryFetchTasksAsync(httpClient, options, stoppingToken);
            }
        }
    }

    private async Task<EdgeEnrollmentResponse?> TryEnrollAsync(HttpClient httpClient, EdgeControlPlaneOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var request = BuildEnrollmentRequest(options);
            using var response = await httpClient.PostAsJsonAsync("v1/edge-agents/enroll", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<EdgeEnrollmentResponse>(cancellationToken: cancellationToken);
            _logger.LogInformation(
                "Enrolled edge agent {AgentId} for site {SiteId} on {OperatingSystem}/{Architecture}.",
                request.AgentId,
                request.SiteId,
                request.OperatingSystem,
                request.Architecture);
            return payload;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Edge enrollment with mothership failed.");
            return null;
        }
    }

    private async Task<EdgeHeartbeatResponse?> TryHeartbeatAsync(HttpClient httpClient, EdgeControlPlaneOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var request = BuildHeartbeatRequest(options);
            using var response = await httpClient.PostAsJsonAsync("v1/edge-agents/heartbeat", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<EdgeHeartbeatResponse>(cancellationToken: cancellationToken);
            if (payload is not null)
            {
                _logger.LogDebug(
                    "Heartbeat acknowledged for edge agent {AgentId}; pending task count {PendingTaskCount}.",
                    payload.AgentId,
                    payload.PendingTaskCount);
            }

            return payload;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Edge heartbeat with mothership failed.");
            return null;
        }
    }

    private async Task TryFetchTasksAsync(HttpClient httpClient, EdgeControlPlaneOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync($"v1/edge-agents/{ResolveAgentId(options)}/tasks", cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<PendingTaskResponse>(cancellationToken: cancellationToken);
            if (payload is { Count: > 0 })
            {
                _logger.LogInformation(
                    "Edge agent {AgentId} has {TaskCount} pending authorized tasks waiting for queue-bridge implementation.",
                    ResolveAgentId(options),
                    payload.Count);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Fetching pending authorized tasks from mothership failed.");
        }
    }

    private EdgeEnrollmentRequest BuildEnrollmentRequest(EdgeControlPlaneOptions options)
        => new()
        {
            SiteId = ResolveSiteId(options),
            SiteName = ResolveSiteName(options),
            AgentId = ResolveAgentId(options),
            DisplayName = ResolveDisplayName(options),
            Hostname = Environment.MachineName,
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            AgentVersion = ResolveAgentVersion(),
            Capabilities = ResolveCapabilities(options),
            MetadataJson = JsonSerializer.Serialize(new
            {
                framework = RuntimeInformation.FrameworkDescription,
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                runtimeIdentifier = RuntimeInformation.RuntimeIdentifier,
                shippingTargets = new[] { "linux-x64", "win-x64" }
            })
        };

    private EdgeHeartbeatRequest BuildHeartbeatRequest(EdgeControlPlaneOptions options)
        => new()
        {
            AgentId = ResolveAgentId(options),
            SiteId = ResolveSiteId(options),
            Hostname = Environment.MachineName,
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            AgentVersion = ResolveAgentVersion(),
            Capabilities = ResolveCapabilities(options),
            StatusSummaryJson = JsonSerializer.Serialize(new
            {
                framework = RuntimeInformation.FrameworkDescription,
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                workerRole = Environment.GetEnvironmentVariable("WORKER_ROLE") ?? "all",
                shippingTargets = new[] { "linux-x64", "win-x64" }
            })
        };

    private static Uri BuildBaseUri(string baseUrl)
        => new(baseUrl.EndsWith('/') ? baseUrl : baseUrl + '/', UriKind.Absolute);

    private static string ResolveSiteId(EdgeControlPlaneOptions options)
        => string.IsNullOrWhiteSpace(options.SiteId) ? "default-site" : options.SiteId.Trim();

    private static string ResolveSiteName(EdgeControlPlaneOptions options)
        => string.IsNullOrWhiteSpace(options.SiteName) ? ResolveSiteId(options) : options.SiteName.Trim();

    private static string ResolveAgentId(EdgeControlPlaneOptions options)
        => string.IsNullOrWhiteSpace(options.AgentId)
            ? $"{Environment.MachineName}-{RuntimeInformation.OSArchitecture}".ToLowerInvariant()
            : options.AgentId.Trim().ToLowerInvariant();

    private static string ResolveDisplayName(EdgeControlPlaneOptions options)
        => string.IsNullOrWhiteSpace(options.DisplayName)
            ? Environment.MachineName
            : options.DisplayName.Trim();

    private static string ResolveAgentVersion()
        => typeof(EdgeControlPlaneSyncService).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private IReadOnlyCollection<string> ResolveCapabilities(EdgeControlPlaneOptions options)
    {
        if (options.Capabilities.Count > 0)
        {
            return options.Capabilities
                .Where(capability => !string.IsNullOrWhiteSpace(capability))
                .Select(capability => capability.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var workerRole = Environment.GetEnvironmentVariable("WORKER_ROLE")?.ToLowerInvariant() ?? "all";
        var roles = workerRole
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(role => role.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (roles.Contains("all"))
        {
            return BuildCombinedCapabilities(FullCapabilityCatalog);
        }

        return BuildCombinedCapabilities(roles.Select(role => $"role:{role}"));
    }

    private IReadOnlyCollection<string> BuildCombinedCapabilities(IEnumerable<string> workerCapabilities)
        => workerCapabilities
            .Concat(_assessmentToolCatalog.GetAll().Select(definition => definition.ToolId))
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .Select(capability => capability.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private sealed record PendingTaskResponse
    {
        public int Count { get; init; }
        public IReadOnlyCollection<AuthorizedTaskDescriptor> Results { get; init; } = Array.Empty<AuthorizedTaskDescriptor>();
    }
}
