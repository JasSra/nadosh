using Nadosh.Core.Interfaces;
using Nadosh.Core.Configuration;
using Nadosh.Core.Models;
using Nadosh.Core.Services;
using Nadosh.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Nadosh.Workers.Rules;
using Nadosh.Workers.Queue;

namespace Nadosh.Workers;

public class Stage2Worker : BackgroundService
{
    private static readonly string WorkerId = $"{Environment.MachineName}/stage2/{Environment.ProcessId}";
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Stage2Worker> _logger;

    public Stage2Worker(IServiceProvider serviceProvider, ILogger<Stage2Worker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Stage2Worker starting...");
        
        using var scope = _serviceProvider.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue<Stage2EnrichmentJob>>();
        var queuePolicy = scope.ServiceProvider.GetRequiredService<IQueuePolicyProvider>().GetPolicy<Stage2EnrichmentJob>();

        while (!stoppingToken.IsCancellationRequested)
        {
            var msg = await queue.DequeueAsync(queuePolicy.VisibilityTimeout, stoppingToken);
            if (msg == null)
            {
                await Task.Delay(2000, stoppingToken);
                continue;
            }

            try
            {
                await QueueProcessingUtilities.RunWithLeaseHeartbeatAsync(
                    queue,
                    msg,
                    queuePolicy,
                    ct => ProcessJobAsync(msg, scope.ServiceProvider, ct),
                    _logger,
                    stoppingToken);
                await queue.AcknowledgeAsync(msg, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Stage 2 enrichment job");
                await TrackAuthorizedTaskFailureAsync(scope.ServiceProvider, msg.Payload, ex, stoppingToken);
                await HandleFailureAsync(msg, scope.ServiceProvider, queue, queuePolicy, ex, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(JobQueueMessage<Stage2EnrichmentJob> message, IServiceProvider scopedProvider, CancellationToken ct)
    {
        var job = message.Payload;
        await ValidateAuthorizedTaskScopeAsync(job, scopedProvider, ct);
        _logger.LogInformation($"Running enrichments for {job.TargetIp} - Rules: {string.Join(", ", job.RuleIds)}");

        var db = scopedProvider.GetRequiredService<NadoshDbContext>();
        var handoffDispatchService = scopedProvider.GetRequiredService<IObservationHandoffDispatchService>();
        var pipelineStateService = scopedProvider.GetRequiredService<IObservationPipelineStateService>();
        var ruleRepository = scopedProvider.GetRequiredService<IRuleConfigRepository>();
        var ruleExecutionService = scopedProvider.GetRequiredService<IRuleExecutionService>();

        var observationId = long.Parse(job.ObservationId);
        var sourceObservation = await db.Observations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == observationId, ct);

        if (sourceObservation == null)
        {
            throw new InvalidOperationException($"Observation {job.ObservationId} was not found for Stage 2 enrichment.");
        }

        var handoffStartResult = await handoffDispatchService.StartAsync(
            ObservationHandoffDispatchKind.Stage2Enrichment,
            observationId,
            sourceObservation.ScanRunId ?? string.Empty,
            sourceObservation.TargetId,
            sourceObservation.Port,
            sourceObservation.Protocol,
            sourceObservation.ServiceName,
            WorkerId,
            ct);

        if (handoffStartResult.Status == ObservationHandoffDispatchTransitionStatus.Rejected)
        {
            _logger.LogInformation(
                "Skipping Stage 2 job for observation {ObservationId}: {Reason}",
                observationId,
                handoffStartResult.Reason);
            return;
        }

        if (handoffStartResult.Status == ObservationHandoffDispatchTransitionStatus.NoOp)
        {
            _logger.LogInformation(
                "Skipping duplicate Stage 2 job for observation {ObservationId}: {Reason}",
                observationId,
                handoffStartResult.Reason);
            return;
        }

        if (handoffStartResult.PreviousState == ObservationHandoffDispatchState.Error)
        {
            var retryTransition = await pipelineStateService.RetryAsync(
                observationId,
                ObservationPipelineState.Stage2Queued,
                WorkerId,
                ct);

            if (retryTransition.Status == ObservationPipelineTransitionStatus.Rejected)
            {
                throw new InvalidOperationException(
                    $"Could not requeue observation {observationId} for Stage 2 retry: {retryTransition.Reason}");
            }
        }

        var stage2StartTransition = await pipelineStateService.TransitionAsync(
            observationId,
            ObservationPipelineState.Stage2Processing,
            WorkerId,
            ct);

        if (stage2StartTransition.Status == ObservationPipelineTransitionStatus.NotFound)
        {
            throw new InvalidOperationException($"Observation {job.ObservationId} was not found for Stage 2 state tracking.");
        }

        if (stage2StartTransition.Status == ObservationPipelineTransitionStatus.Rejected)
        {
            await handoffDispatchService.CompleteAsync(
                ObservationHandoffDispatchKind.Stage2Enrichment,
                observationId,
                observationId,
                WorkerId,
                ct);

            _logger.LogInformation(
                "Skipping Stage 2 job for observation {ObservationId} because the state has already advanced: {Reason}",
                observationId,
                stage2StartTransition.Reason);
            return;
        }

        var activeRules = await ruleRepository.GetActiveRulesAsync(job.RuleIds, ct);
        var activeRulesById = activeRules.ToDictionary(r => r.RuleId, StringComparer.OrdinalIgnoreCase);

        var missingRules = job.RuleIds
            .Where(ruleId => !activeRulesById.ContainsKey(ruleId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missingRules.Count > 0)
        {
            throw new InvalidOperationException($"No active RuleConfig found for: {string.Join(", ", missingRules)}");
        }

        var hasHardFailure = false;
        
        foreach (var ruleId in job.RuleIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var rule = activeRulesById[ruleId];

            var existing = await db.EnrichmentResults
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.ObservationId == observationId
                      && r.RuleId == rule.RuleId
                      && r.RuleVersion == rule.Version,
                    ct);

            if (existing != null)
            {
                _logger.LogInformation(
                    "Skipping Stage 2 enrichment for {TargetIp} rule {RuleId} v{RuleVersion} because it already exists.",
                    job.TargetIp,
                    rule.RuleId,
                    rule.Version);
                continue;
            }

            var outcome = await ruleExecutionService.ExecuteAsync(rule, sourceObservation, ct);
            var tags = outcome.Tags
                .Concat(string.IsNullOrWhiteSpace(outcome.Severity) ? [] : [outcome.Severity!])
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            
            var result = new EnrichmentResult
            {
                ObservationId = observationId,
                RuleId = rule.RuleId,
                RuleVersion = rule.Version,
                ExecutedAt = DateTime.UtcNow,
                ResultStatus = outcome.ResultStatus,
                Confidence = (float)outcome.Confidence,
                Summary = outcome.Summary,
                EvidenceJson = outcome.EvidenceJson,
                Tags = tags
            };

            db.EnrichmentResults.Add(result);
            hasHardFailure |= outcome.ResultStatus is "error" or "timeout" or "config-error";
            _logger.LogInformation(
                "Enrichment {RuleId} v{RuleVersion} on {TargetIp} completed with status {Status}.",
                rule.RuleId,
                rule.Version,
                job.TargetIp,
                outcome.ResultStatus);
        }

        await db.SaveChangesAsync(ct);

        if (hasHardFailure)
        {
            throw new InvalidOperationException($"Stage 2 rule execution encountered hard failures for observation {observationId}.");
        }

        var enrichedTransition = await pipelineStateService.TransitionAsync(
            observationId,
            ObservationPipelineState.Enriched,
            WorkerId,
            ct);

        if (enrichedTransition.Status == ObservationPipelineTransitionStatus.Applied
            || enrichedTransition.Status == ObservationPipelineTransitionStatus.NoOp)
        {
            await pipelineStateService.TransitionAsync(
                observationId,
                ObservationPipelineState.Completed,
                WorkerId,
                ct);

            await handoffDispatchService.CompleteAsync(
                ObservationHandoffDispatchKind.Stage2Enrichment,
                observationId,
                observationId,
                WorkerId,
                ct);

            await TrackAuthorizedTaskSuccessAsync(scopedProvider, job, ct);
        }
    }

    private async Task HandleFailureAsync(
        JobQueueMessage<Stage2EnrichmentJob> message,
        IServiceProvider scopedProvider,
        IJobQueue<Stage2EnrichmentJob> queue,
        ResolvedQueuePolicy queuePolicy,
        Exception exception,
        CancellationToken ct)
    {
        var observationId = long.TryParse(message.Payload.ObservationId, out var parsedObservationId)
            ? parsedObservationId
            : (long?)null;

        using var failureScope = scopedProvider.CreateScope();
        var db = failureScope.ServiceProvider.GetRequiredService<NadoshDbContext>();
        var handoffDispatchService = failureScope.ServiceProvider.GetRequiredService<IObservationHandoffDispatchService>();
        var pipelineStateService = failureScope.ServiceProvider.GetRequiredService<IObservationPipelineStateService>();

        if (observationId.HasValue)
        {
            await handoffDispatchService.FailAsync(
                ObservationHandoffDispatchKind.Stage2Enrichment,
                observationId.Value,
                WorkerId,
                exception.Message,
                cancellationToken: ct);

            await pipelineStateService.TransitionAsync(
                observationId.Value,
                ObservationPipelineState.Error,
                WorkerId,
                ct);
        }

        if (observationId.HasValue)
        {
            var sourceObservation = await db.Observations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == observationId.Value, ct);

            await handoffDispatchService.ScheduleAsync(
                ObservationHandoffDispatchKind.Stage2Enrichment,
                observationId.Value,
                sourceObservation?.ScanRunId ?? string.Empty,
                sourceObservation?.TargetId ?? message.Payload.TargetIp,
                sourceObservation?.Port ?? 0,
                sourceObservation?.Protocol ?? "tcp",
                sourceObservation?.ServiceName,
                WorkerId,
                ct);

            await pipelineStateService.RetryAsync(
                observationId.Value,
                ObservationPipelineState.Stage2Queued,
                WorkerId,
                ct);
        }

        if (message.AttemptCount >= queuePolicy.MaxAttempts)
        {
            await queue.DeadLetterAsync(message, exception.Message, ct);
            return;
        }

        var retryDelay = QueueProcessingUtilities.ComputeRetryDelay(message.AttemptCount, queuePolicy);
        _logger.LogInformation(
            "Retrying Stage 2 job for observation {ObservationId} after {DelaySeconds}s.",
            observationId,
            retryDelay.TotalSeconds);

        await queue.RejectAsync(message, reenqueue: true, reenqueueDelay: retryDelay, ct);
    }

    private static async Task ValidateAuthorizedTaskScopeAsync(Stage2EnrichmentJob job, IServiceProvider scopedProvider, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.AuthorizedTaskId))
        {
            return;
        }

        var scope = AuthorizedTaskScopeEvaluator.Parse(job.AuthorizedScopeJson);
        var validation = AuthorizedTaskScopeEvaluator.ValidateTarget(AuthorizedTaskKinds.Stage2Enrichment, scope, job.TargetIp);
        if (!validation.IsAllowed)
        {
            var tracker = scopedProvider.GetRequiredService<IEdgeTaskExecutionTracker>();
            await tracker.MarkFailedAsync(job.AuthorizedTaskId, validation.Reason, metadataJson: job.ApprovalReference, cancellationToken: ct);
            throw new InvalidOperationException(validation.Reason);
        }
    }

    private static async Task TrackAuthorizedTaskSuccessAsync(IServiceProvider scopedProvider, Stage2EnrichmentJob job, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.AuthorizedTaskId))
        {
            return;
        }

        var tracker = scopedProvider.GetRequiredService<IEdgeTaskExecutionTracker>();
        await tracker.MarkCompletedAsync(
            job.AuthorizedTaskId,
            $"Stage 2 enrichment completed for observation {job.ObservationId}.",
            metadataJson: System.Text.Json.JsonSerializer.Serialize(new { job.ObservationId, job.TargetIp, ruleCount = job.RuleIds.Count }),
            cancellationToken: ct);
    }

    private static async Task TrackAuthorizedTaskFailureAsync(IServiceProvider scopedProvider, Stage2EnrichmentJob job, Exception exception, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.AuthorizedTaskId))
        {
            return;
        }

        var tracker = scopedProvider.GetRequiredService<IEdgeTaskExecutionTracker>();
        await tracker.MarkFailedAsync(job.AuthorizedTaskId, exception.Message, requeueRecommended: false, metadataJson: job.ApprovalReference, cancellationToken: ct);
    }
}
