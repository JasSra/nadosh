using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Nadosh.Core.Configuration;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Workers.Edge;

public sealed class EdgeTaskUploadService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEdgeTaskExecutionTracker _executionTracker;
    private readonly IOptionsMonitor<EdgeControlPlaneOptions> _optionsMonitor;
    private readonly ILogger<EdgeTaskUploadService> _logger;

    public EdgeTaskUploadService(
        IHttpClientFactory httpClientFactory,
        IEdgeTaskExecutionTracker executionTracker,
        IOptionsMonitor<EdgeControlPlaneOptions> optionsMonitor,
        ILogger<EdgeTaskUploadService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _executionTracker = executionTracker;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            _logger.LogInformation("Edge task upload service is disabled.");
            return;
        }

        using var httpClient = _httpClientFactory.CreateClient();
        httpClient.BaseAddress = new Uri(options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + '/');
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            httpClient.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pendingUploads = await _executionTracker.GetPendingUploadsAsync(50, stoppingToken);
                foreach (var record in pendingUploads)
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await UploadRecordAsync(httpClient, options, record, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Edge task upload sweep failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private async Task UploadRecordAsync(HttpClient httpClient, EdgeControlPlaneOptions options, EdgeTaskExecutionRecord record, CancellationToken cancellationToken)
    {
        try
        {
            HttpResponseMessage response;
            if (record.Status == EdgeTaskExecutionStatus.CompletedLocal)
            {
                response = await httpClient.PostAsJsonAsync(
                    $"v1/edge-agents/{ResolveAgentId(options)}/tasks/{record.AuthorizedTaskId}/complete",
                    new EdgeTaskCompletionRequest
                    {
                        LeaseToken = record.LeaseToken,
                        ResultStatus = "completed-local",
                        Summary = record.Summary ?? "Task completed locally.",
                        MetadataJson = record.MetadataJson
                    },
                    cancellationToken);
            }
            else
            {
                response = await httpClient.PostAsJsonAsync(
                    $"v1/edge-agents/{ResolveAgentId(options)}/tasks/{record.AuthorizedTaskId}/fail",
                    new EdgeTaskFailureRequest
                    {
                        LeaseToken = record.LeaseToken,
                        ErrorMessage = record.Summary ?? record.LastUploadError ?? "Task failed locally.",
                        Requeue = record.RequeueRecommended,
                        MetadataJson = record.MetadataJson
                    },
                    cancellationToken);
            }

            response.EnsureSuccessStatusCode();
            await _executionTracker.MarkUploadedAsync(record.Id, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var backoff = TimeSpan.FromSeconds(Math.Min(Math.Max(record.UploadAttemptCount + 1, 1) * 15, 300));
            _logger.LogWarning(ex, "Uploading edge task execution record {RecordId} for task {TaskId} failed; retrying in {Backoff}s.",
                record.Id,
                record.AuthorizedTaskId,
                backoff.TotalSeconds);
            await _executionTracker.MarkUploadDeferredAsync(record.Id, ex.Message, backoff, cancellationToken);
        }
    }

    private static string ResolveAgentId(EdgeControlPlaneOptions options)
        => string.IsNullOrWhiteSpace(options.AgentId)
            ? $"{Environment.MachineName}-{System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}".ToLowerInvariant()
            : options.AgentId.Trim().ToLowerInvariant();
}
