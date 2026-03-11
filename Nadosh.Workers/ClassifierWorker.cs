using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;
using Nadosh.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Nadosh.Workers.Queue;

namespace Nadosh.Workers;

public class ClassifierWorker : BackgroundService
{
    private static readonly string WorkerId = $"{Environment.MachineName}/classifier/{Environment.ProcessId}";
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ClassifierWorker> _logger;
    private readonly IJobQueue<Stage2EnrichmentJob> _enrichmentQueue;
    private readonly IServiceIdentifier _serviceIdentifier;

    public ClassifierWorker(
        IServiceProvider serviceProvider,
        ILogger<ClassifierWorker> logger,
        IJobQueue<Stage2EnrichmentJob> enrichmentQueue,
        IServiceIdentifier serviceIdentifier)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _enrichmentQueue = enrichmentQueue;
        _serviceIdentifier = serviceIdentifier;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ClassifierWorker starting...");
        
        using var scope = _serviceProvider.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue<ClassificationJob>>();
        var queuePolicy = scope.ServiceProvider.GetRequiredService<IQueuePolicyProvider>().GetPolicy<ClassificationJob>();

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
                    ct => ProcessJobAsync(msg.Payload, scope.ServiceProvider, ct),
                    _logger,
                    stoppingToken);
                await queue.AcknowledgeAsync(msg, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing classification job");
                await QueueProcessingUtilities.RejectWithBackoffOrDeadLetterAsync(queue, msg, ex, _logger, queuePolicy, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(ClassificationJob job, IServiceProvider scopedProvider, CancellationToken ct)
    {
        var obs = job.Observation;
        var handoffDispatchService = scopedProvider.GetRequiredService<IObservationHandoffDispatchService>();
        var pipelineStateService = scopedProvider.GetRequiredService<IObservationPipelineStateService>();
        var handoffStartResult = await handoffDispatchService.StartAsync(
            ObservationHandoffDispatchKind.Classification,
            obs.Id,
            obs.ScanRunId ?? string.Empty,
            obs.TargetId,
            obs.Port,
            obs.Protocol,
            obs.ServiceName,
            WorkerId,
            ct);

        if (handoffStartResult.Status == ObservationHandoffDispatchTransitionStatus.Rejected)
        {
            _logger.LogInformation(
                "Skipping classification job for observation {ObservationId}: {Reason}",
                obs.Id,
                handoffStartResult.Reason);
            return;
        }

        if (handoffStartResult.Status == ObservationHandoffDispatchTransitionStatus.NoOp)
        {
            _logger.LogInformation(
                "Skipping duplicate classification job for observation {ObservationId}: {Reason}",
                obs.Id,
                handoffStartResult.Reason);
            return;
        }

        try
        {

            // Use IServiceIdentifier for classification
            string classification = obs.ServiceName
                ?? _serviceIdentifier.IdentifyByPort(obs.Port)
                ?? "unknown";
            string severity = _serviceIdentifier.ClassifySeverity(obs.Port, obs.State, classification);
            var ruleIdsToTrigger = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Determine enrichment rules based on service type
            if (obs.State == "open")
            {
                if (IsHttpLike(classification, obs.Port))
                    ruleIdsToTrigger.Add("http-title-check");

                if (IsTlsLike(classification, obs.Port))
                    ruleIdsToTrigger.Add("tls-cert-check");

                if (IsSshLike(classification, obs.Port))
                    ruleIdsToTrigger.Add("ssh-banner-check");

                if (IsRdpLike(classification, obs.Port))
                    ruleIdsToTrigger.Add("rdp-presence-check");
            }

            var db = scopedProvider.GetRequiredService<NadoshDbContext>();
        
            var exposure = await db.CurrentExposures.FirstOrDefaultAsync(x => x.TargetId == obs.TargetId && x.Port == obs.Port, ct);
            if (exposure == null)
            {
                exposure = new CurrentExposure
                {
                    TargetId = obs.TargetId,
                    Port = obs.Port,
                    Protocol = obs.Protocol,
                    CurrentState = obs.State,
                    FirstSeen = obs.ObservedAt,
                    LastSeen = obs.ObservedAt,
                    LastChanged = obs.ObservedAt,
                    Classification = classification,
                    Severity = severity
                };
                db.CurrentExposures.Add(exposure);
            }
            else
            {
                if (exposure.CurrentState != obs.State)
                {
                    exposure.LastChanged = obs.ObservedAt;
                }
                exposure.CurrentState = obs.State;
                exposure.LastSeen = obs.ObservedAt;
                exposure.Classification = classification;
                exposure.Severity = severity;
            }

            await db.SaveChangesAsync(ct);

            var classifiedTransition = await pipelineStateService.TransitionAsync(
                obs.Id,
                ObservationPipelineState.Classified,
                WorkerId,
                ct);

            if (classifiedTransition.Status == ObservationPipelineTransitionStatus.NotFound)
            {
                throw new InvalidOperationException($"Observation {obs.Id} was not found for classification state tracking.");
            }

            if (classifiedTransition.Status == ObservationPipelineTransitionStatus.Rejected)
            {
                await CompleteClassificationHandoffAsync(handoffDispatchService, obs.Id, ct);
                _logger.LogInformation(
                    "Skipping duplicate/out-of-order classification transition for observation {ObservationId}: {Reason}",
                    obs.Id,
                    classifiedTransition.Reason);
                return;
            }

            _logger.LogInformation($"Classified {obs.TargetId}:{obs.Port} as {classification} ({obs.State})");

            if (ruleIdsToTrigger.Any())
            {
                var flaggedTransition = await pipelineStateService.TransitionAsync(
                    obs.Id,
                    ObservationPipelineState.FlaggedForEnrichment,
                    WorkerId,
                    ct);

                if (flaggedTransition.Status == ObservationPipelineTransitionStatus.NotFound)
                {
                    throw new InvalidOperationException($"Observation {obs.Id} disappeared before enrichment flagging.");
                }

                if (flaggedTransition.Status == ObservationPipelineTransitionStatus.Rejected)
                {
                    await CompleteClassificationHandoffAsync(handoffDispatchService, obs.Id, ct);
                    _logger.LogInformation(
                        "Skipping Stage 2 enqueue for observation {ObservationId} because state has already advanced: {Reason}",
                        obs.Id,
                        flaggedTransition.Reason);
                    return;
                }

                var stage2HandoffScheduleResult = await handoffDispatchService.ScheduleAsync(
                    ObservationHandoffDispatchKind.Stage2Enrichment,
                    obs.Id,
                    obs.ScanRunId ?? string.Empty,
                    obs.TargetId,
                    obs.Port,
                    obs.Protocol,
                    classification,
                    WorkerId,
                    ct);

                if (stage2HandoffScheduleResult.Status == ObservationHandoffDispatchTransitionStatus.Rejected)
                {
                    throw new InvalidOperationException(
                        $"Stage 2 handoff scheduling was rejected for observation {obs.Id}: {stage2HandoffScheduleResult.Reason}");
                }

                if (stage2HandoffScheduleResult.Status == ObservationHandoffDispatchTransitionStatus.NoOp)
                {
                    await CompleteClassificationHandoffAsync(handoffDispatchService, obs.Id, ct);
                    _logger.LogInformation(
                        "Skipping Stage 2 enqueue for observation {ObservationId} because a Stage 2 handoff already exists.",
                        obs.Id);
                    return;
                }

                var stage2Job = new Stage2EnrichmentJob
                {
                    ObservationId = obs.Id.ToString(),
                    TargetIp = obs.TargetId,
                    RuleIds = ruleIdsToTrigger.ToList()
                };

                try
                {
                    await _enrichmentQueue.EnqueueAsync(
                        stage2Job,
                        idempotencyKey: $"stage2:{obs.Id}",
                        priority: 0,
                        enqueueOptions: new JobEnqueueOptions { ShardKey = obs.TargetId },
                        cancellationToken: ct);

                    var queuedTransition = await pipelineStateService.TransitionAsync(
                        obs.Id,
                        ObservationPipelineState.Stage2Queued,
                        WorkerId,
                        ct);

                    if (queuedTransition.Status == ObservationPipelineTransitionStatus.NotFound)
                    {
                        throw new InvalidOperationException($"Observation {obs.Id} disappeared before Stage 2 queue tracking.");
                    }

                    if (queuedTransition.Status == ObservationPipelineTransitionStatus.Rejected)
                    {
                        _logger.LogInformation(
                            "Stage 2 queue transition was rejected for observation {ObservationId}: {Reason}",
                            obs.Id,
                            queuedTransition.Reason);
                    }
                }
                catch (Exception ex)
                {
                    await handoffDispatchService.FailAsync(
                        ObservationHandoffDispatchKind.Stage2Enrichment,
                        obs.Id,
                        WorkerId,
                        ex.Message,
                        cancellationToken: ct);

                    await pipelineStateService.TransitionAsync(
                        obs.Id,
                        ObservationPipelineState.Error,
                        WorkerId,
                        ct);
                    throw;
                }

                await CompleteClassificationHandoffAsync(handoffDispatchService, obs.Id, ct);
                _logger.LogInformation($"Enqueued Stage 2 enrichment for {obs.TargetId}:{obs.Port} with rules: {string.Join(",", ruleIdsToTrigger)}");
                return;
            }

            var completedTransition = await pipelineStateService.TransitionAsync(
                obs.Id,
                ObservationPipelineState.Completed,
                WorkerId,
                ct);

            if (completedTransition.Status == ObservationPipelineTransitionStatus.Rejected)
            {
                _logger.LogDebug(
                    "Completion transition skipped for observation {ObservationId}: {Reason}",
                    obs.Id,
                    completedTransition.Reason);
            }

            await CompleteClassificationHandoffAsync(handoffDispatchService, obs.Id, ct);
        }
        catch (Exception ex)
        {
            await handoffDispatchService.FailAsync(
                ObservationHandoffDispatchKind.Classification,
                obs.Id,
                WorkerId,
                ex.Message,
                cancellationToken: ct);
            throw;
        }
    }

    private static async Task CompleteClassificationHandoffAsync(
        IObservationHandoffDispatchService handoffDispatchService,
        long observationId,
        CancellationToken cancellationToken)
    {
        await handoffDispatchService.CompleteAsync(
            ObservationHandoffDispatchKind.Classification,
            observationId,
            observationId,
            WorkerId,
            cancellationToken);
    }

    private static bool IsHttpLike(string classification, int port)
        => classification.Contains("http", StringComparison.OrdinalIgnoreCase)
           || classification.Contains("web", StringComparison.OrdinalIgnoreCase)
           || port is 80 or 443 or 8080 or 8000 or 8008 or 8081 or 8443 or 8888 or 9443;

    private static bool IsTlsLike(string classification, int port)
        => classification.Contains("https", StringComparison.OrdinalIgnoreCase)
           || classification.Contains("tls", StringComparison.OrdinalIgnoreCase)
           || port is 443 or 8443 or 9443;

    private static bool IsSshLike(string classification, int port)
        => classification.Contains("ssh", StringComparison.OrdinalIgnoreCase)
           || port == 22;

    private static bool IsRdpLike(string classification, int port)
        => classification.Contains("rdp", StringComparison.OrdinalIgnoreCase)
           || port == 3389;
}
