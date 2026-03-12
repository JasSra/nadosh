using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Nadosh.Core.Configuration;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;
using Nadosh.Core.Services;

namespace Nadosh.Workers.Edge;

public sealed class EdgeControlPlaneSyncService : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

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
    private readonly IEdgeTaskExecutionTracker _executionTracker;
    private readonly IJobQueue<Stage1ScanJob> _stage1Queue;
    private readonly IJobQueue<Stage2EnrichmentJob> _stage2Queue;
    private readonly IJobQueue<MacEnrichmentJob> _macEnrichmentQueue;
    private readonly IOptionsMonitor<EdgeControlPlaneOptions> _optionsMonitor;
    private readonly ILogger<EdgeControlPlaneSyncService> _logger;

    public EdgeControlPlaneSyncService(
        IHttpClientFactory httpClientFactory,
        IAssessmentToolCatalog assessmentToolCatalog,
        IEdgeTaskExecutionTracker executionTracker,
        IJobQueue<Stage1ScanJob> stage1Queue,
        IJobQueue<Stage2EnrichmentJob> stage2Queue,
        IJobQueue<MacEnrichmentJob> macEnrichmentQueue,
        IOptionsMonitor<EdgeControlPlaneOptions> optionsMonitor,
        ILogger<EdgeControlPlaneSyncService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _assessmentToolCatalog = assessmentToolCatalog;
        _executionTracker = executionTracker;
        _stage1Queue = stage1Queue;
        _stage2Queue = stage2Queue;
        _macEnrichmentQueue = macEnrichmentQueue;
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
                    "Edge agent {AgentId} has {TaskCount} pending authorized tasks to bridge locally.",
                    ResolveAgentId(options),
                    payload.Count);

                await ProcessPendingTasksAsync(httpClient, options, payload.Results, cancellationToken);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Fetching pending authorized tasks from mothership failed.");
        }
    }

    private async Task ProcessPendingTasksAsync(
        HttpClient httpClient,
        EdgeControlPlaneOptions options,
        IReadOnlyCollection<AuthorizedTaskDescriptor> tasks,
        CancellationToken cancellationToken)
    {
        var localCapabilities = ResolveCapabilities(options)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var task in tasks.OrderByDescending(task => task.Priority).ThenBy(task => task.IssuedAt))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (task.RequiredCapabilities.Count > 0 && !task.RequiredCapabilities.All(localCapabilities.Contains))
            {
                _logger.LogWarning(
                    "Skipping authorized task {TaskId} because local capabilities do not satisfy all requirements.",
                    task.TaskId);
                continue;
            }

            var claim = await TryClaimTaskAsync(httpClient, options, task.TaskId, cancellationToken);
            if (claim is null)
            {
                continue;
            }

            try
            {
                var dispatchResult = await DispatchClaimedTaskAsync(claim, cancellationToken);
                if (!dispatchResult.IsSuccess)
                {
                    await _executionTracker.RecordClaimFailureAsync(
                        claim,
                        dispatchResult.Reason,
                        dispatchResult.RequeueRecommended,
                        dispatchResult.MetadataJson,
                        cancellationToken);
                    continue;
                }

                await _executionTracker.RecordQueuedAsync(
                    claim,
                    dispatchResult.LocalQueueName ?? claim.Task.TaskKind,
                    dispatchResult.LocalJobReference ?? claim.Task.TaskId,
                    dispatchResult.Reason,
                    dispatchResult.MetadataJson,
                    cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Dispatching authorized task {TaskId} failed after claim.", claim.TaskId);
                await _executionTracker.RecordClaimFailureAsync(
                    claim,
                    ex.Message,
                    requeueRecommended: true,
                    metadataJson: JsonSerializer.Serialize(new { claim.Task.TaskKind }),
                    cancellationToken: cancellationToken);
            }
        }
    }

    private async Task<EdgeTaskClaimResponse?> TryClaimTaskAsync(HttpClient httpClient, EdgeControlPlaneOptions options, string taskId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"v1/edge-agents/{ResolveAgentId(options)}/tasks/{taskId}/claim",
                new EdgeTaskClaimRequest { SiteId = ResolveSiteId(options) },
                cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                _logger.LogDebug("Authorized task {TaskId} could not be claimed because it is no longer available.", taskId);
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<EdgeTaskClaimResponse>(cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Claiming authorized task {TaskId} from mothership failed.", taskId);
            return null;
        }
    }

    private async Task<LocalDispatchResult> DispatchClaimedTaskAsync(EdgeTaskClaimResponse claim, CancellationToken cancellationToken)
    {
        var scope = AuthorizedTaskScopeEvaluator.Parse(claim.Task.ScopeJson);

        switch (claim.Task.TaskKind.Trim().ToLowerInvariant())
        {
            case AuthorizedTaskKinds.Stage1Scan:
            {
                var job = DeserializePayload<Stage1ScanJob>(claim.Task.PayloadJson, claim.Task.TaskKind);
                if (job is null)
                {
                    return LocalDispatchResult.Fail($"Payload for {claim.Task.TaskKind} could not be parsed.");
                }

                var scopeValidation = AuthorizedTaskScopeEvaluator.ValidateTarget(claim.Task.TaskKind, scope, job.TargetIp, job.PortsToScan);
                if (!scopeValidation.IsAllowed)
                {
                    return LocalDispatchResult.Fail(scopeValidation.Reason);
                }

                if (string.IsNullOrWhiteSpace(job.BatchId))
                {
                    job.BatchId = $"edge-{claim.TaskId}";
                }

                job.AuthorizedTaskId = claim.TaskId;
                job.AuthorizedScopeJson = claim.Task.ScopeJson;
                job.ApprovalReference = claim.Task.ApprovalReference;

                await _stage1Queue.EnqueueAsync(
                    job,
                    idempotencyKey: $"edge-task:{claim.TaskId}",
                    priority: claim.Task.Priority,
                    enqueueOptions: new JobEnqueueOptions { ShardKey = job.TargetIp },
                    cancellationToken: cancellationToken);

                return LocalDispatchResult.Success(
                    $"Queued {AuthorizedTaskKinds.Stage1Scan} for {job.TargetIp} ({job.PortsToScan.Count} ports).",
                    JsonSerializer.Serialize(new { localQueue = nameof(Stage1ScanJob), job.TargetIp, job.BatchId, portCount = job.PortsToScan.Count }),
                    localQueueName: nameof(Stage1ScanJob),
                    localJobReference: job.BatchId);
            }
            case AuthorizedTaskKinds.Stage2Enrichment:
            {
                var job = DeserializePayload<Stage2EnrichmentJob>(claim.Task.PayloadJson, claim.Task.TaskKind);
                if (job is null || string.IsNullOrWhiteSpace(job.ObservationId))
                {
                    return LocalDispatchResult.Fail($"Payload for {claim.Task.TaskKind} is missing the required observation reference.");
                }

                var scopeValidation = AuthorizedTaskScopeEvaluator.ValidateTarget(claim.Task.TaskKind, scope, job.TargetIp);
                if (!scopeValidation.IsAllowed)
                {
                    return LocalDispatchResult.Fail(scopeValidation.Reason);
                }

                job.AuthorizedTaskId = claim.TaskId;
                job.AuthorizedScopeJson = claim.Task.ScopeJson;
                job.ApprovalReference = claim.Task.ApprovalReference;

                await _stage2Queue.EnqueueAsync(
                    job,
                    idempotencyKey: $"edge-task:{claim.TaskId}",
                    priority: claim.Task.Priority,
                    enqueueOptions: new JobEnqueueOptions { ShardKey = job.TargetIp },
                    cancellationToken: cancellationToken);

                return LocalDispatchResult.Success(
                    $"Queued {AuthorizedTaskKinds.Stage2Enrichment} for observation {job.ObservationId}.",
                    JsonSerializer.Serialize(new { localQueue = nameof(Stage2EnrichmentJob), job.TargetIp, job.ObservationId, ruleCount = job.RuleIds.Count }),
                    localQueueName: nameof(Stage2EnrichmentJob),
                    localJobReference: job.ObservationId);
            }
            case AuthorizedTaskKinds.MacEnrichment:
            {
                var job = DeserializePayload<MacEnrichmentJob>(claim.Task.PayloadJson, claim.Task.TaskKind);
                if (job is null)
                {
                    return LocalDispatchResult.Fail($"Payload for {claim.Task.TaskKind} could not be parsed.");
                }

                var scopeValidation = AuthorizedTaskScopeEvaluator.ValidateTarget(claim.Task.TaskKind, scope, job.TargetIp);
                if (!scopeValidation.IsAllowed)
                {
                    return LocalDispatchResult.Fail(scopeValidation.Reason);
                }

                job.AuthorizedTaskId = claim.TaskId;
                job.AuthorizedScopeJson = claim.Task.ScopeJson;
                job.ApprovalReference = claim.Task.ApprovalReference;

                await _macEnrichmentQueue.EnqueueAsync(
                    job,
                    idempotencyKey: $"edge-task:{claim.TaskId}",
                    priority: claim.Task.Priority,
                    enqueueOptions: new JobEnqueueOptions { ShardKey = job.TargetIp },
                    cancellationToken: cancellationToken);

                return LocalDispatchResult.Success(
                    $"Queued {AuthorizedTaskKinds.MacEnrichment} for {job.TargetIp}.",
                    JsonSerializer.Serialize(new { localQueue = nameof(MacEnrichmentJob), job.TargetIp }),
                    localQueueName: nameof(MacEnrichmentJob),
                    localJobReference: job.TargetIp);
            }
            default:
                return LocalDispatchResult.Fail($"Task kind '{claim.Task.TaskKind}' is not supported by the local bridge.");
        }
    }

    private TPayload? DeserializePayload<TPayload>(string payloadJson, string taskKind)
    {
        try
        {
            return JsonSerializer.Deserialize<TPayload>(payloadJson, SerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Payload deserialization failed for authorized task kind {TaskKind}.", taskKind);
            return default;
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

    private sealed record LocalDispatchResult
    {
        public bool IsSuccess { get; init; }
        public string Reason { get; init; } = string.Empty;
        public string? MetadataJson { get; init; }
        public string? LocalQueueName { get; init; }
        public string? LocalJobReference { get; init; }
        public bool RequeueRecommended { get; init; }

        public static LocalDispatchResult Success(string reason, string? metadataJson = null, string? localQueueName = null, string? localJobReference = null)
            => new() { IsSuccess = true, Reason = reason, MetadataJson = metadataJson, LocalQueueName = localQueueName, LocalJobReference = localJobReference };

        public static LocalDispatchResult Fail(string reason, bool requeueRecommended = false)
            => new() { IsSuccess = false, Reason = reason, RequeueRecommended = requeueRecommended };
    }
}
