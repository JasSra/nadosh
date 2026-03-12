using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Workers;

public sealed class AssessmentRunWorker : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly string WorkerId = $"{Environment.MachineName}/assessment-runs/{Environment.ProcessId}";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AssessmentRunWorker> _logger;

    public AssessmentRunWorker(IServiceProvider serviceProvider, ILogger<AssessmentRunWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[{WorkerId}] Assessment run worker starting...", WorkerId);
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQueuedRunsAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("[{WorkerId}] Assessment run worker shutting down.", WorkerId);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{WorkerId}] Fatal assessment run worker error.", WorkerId);
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task ProcessQueuedRunsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var runRepository = scope.ServiceProvider.GetRequiredService<IAssessmentRunRepository>();
        var evidenceService = scope.ServiceProvider.GetRequiredService<IAssessmentEvidenceService>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var toolCatalog = scope.ServiceProvider.GetRequiredService<IAssessmentToolCatalog>();

        var queuedRuns = await runRepository.GetByStatusAsync(AssessmentRunStatus.Queued, 100, cancellationToken);
        if (queuedRuns.Count == 0)
        {
            return;
        }

        _logger.LogInformation("[{WorkerId}] Processing {Count} queued assessment runs.", WorkerId, queuedRuns.Count);

        foreach (var run in queuedRuns)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await ProcessRunAsync(run, runRepository, evidenceService, auditService, toolCatalog, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{WorkerId}] Failed processing assessment run {RunId}.", WorkerId, run.RunId);
            }
        }
    }

    private async Task ProcessRunAsync(
        AssessmentRun run,
        IAssessmentRunRepository runRepository,
        IAssessmentEvidenceService evidenceService,
        IAuditService auditService,
        IAssessmentToolCatalog toolCatalog,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        run.Status = AssessmentRunStatus.InProgress;
        run.StartedAt ??= now;
        run.UpdatedAt = now;
        await runRepository.UpdateAsync(run, cancellationToken);

        var tool = toolCatalog.GetById(run.ToolId);
        var isPassiveOrDryRun = run.DryRun || tool is { ExecutionMode: not AssessmentToolExecutionMode.ActiveValidation };

        if (isPassiveOrDryRun)
        {
            var bundle = await evidenceService.BuildAsync(run.RunId, cancellationToken);
            run.ResultSummaryJson = JsonSerializer.Serialize(new
            {
                outcome = "completed",
                mode = run.DryRun ? "dry-run" : tool?.ExecutionMode.ToString() ?? "unknown",
                evidence = new
                {
                    exposures = bundle?.ExposureCount ?? 0,
                    observations = bundle?.ObservationCount ?? 0,
                    enrichments = bundle?.EnrichmentCount ?? 0
                },
                notes = bundle?.Notes ?? Array.Empty<string>()
            }, SerializerOptions);
            run.Status = AssessmentRunStatus.Completed;
            run.CompletedAt = DateTime.UtcNow;
            run.UpdatedAt = run.CompletedAt.Value;

            await runRepository.UpdateAsync(run, cancellationToken);
            await auditService.WriteAsync(
                actor: WorkerId,
                action: "assessment-run-completed",
                entityType: nameof(AssessmentRun),
                entityId: run.RunId,
                newValue: new { run.Status, run.CompletedAt, run.ResultSummaryJson },
                cancellationToken: cancellationToken);

            _logger.LogInformation("[{WorkerId}] Completed assessment run {RunId} using passive/dry-run flow.", WorkerId, run.RunId);
            return;
        }

        run.ResultSummaryJson = JsonSerializer.Serialize(new
        {
            outcome = "failed",
            reason = "active-validation-execution-not-implemented",
            message = "This run requires an active validation execution adapter that has not been implemented yet."
        }, SerializerOptions);
        run.Status = AssessmentRunStatus.Failed;
        run.CompletedAt = DateTime.UtcNow;
        run.UpdatedAt = run.CompletedAt.Value;

        await runRepository.UpdateAsync(run, cancellationToken);
        await auditService.WriteAsync(
            actor: WorkerId,
            action: "assessment-run-failed",
            entityType: nameof(AssessmentRun),
            entityId: run.RunId,
            newValue: new { run.Status, run.CompletedAt, run.ResultSummaryJson },
            cancellationToken: cancellationToken);

        _logger.LogWarning(
            "[{WorkerId}] Assessment run {RunId} requires an active validation adapter and was marked failed for now.",
            WorkerId,
            run.RunId);
    }
}
