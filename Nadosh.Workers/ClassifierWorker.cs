using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;
using Nadosh.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Nadosh.Workers;

public class ClassifierWorker : BackgroundService
{
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

        while (!stoppingToken.IsCancellationRequested)
        {
            var msg = await queue.DequeueAsync(TimeSpan.FromMinutes(1), stoppingToken);
            if (msg == null)
            {
                await Task.Delay(2000, stoppingToken);
                continue;
            }

            try
            {
                await ProcessJobAsync(msg.Payload, scope.ServiceProvider, stoppingToken);
                await queue.AcknowledgeAsync(msg, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing classification job");
                await queue.DeadLetterAsync(msg, ex.Message, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(ClassificationJob job, IServiceProvider scopedProvider, CancellationToken ct)
    {
        var obs = job.Observation;

        // Use IServiceIdentifier for classification
        string classification = obs.ServiceName
            ?? _serviceIdentifier.IdentifyByPort(obs.Port)
            ?? "unknown";
        string severity = _serviceIdentifier.ClassifySeverity(obs.Port, obs.State, classification);
        var ruleIdsToTrigger = new List<string>();

        // Determine enrichment rules based on service type
        if (obs.State == "open")
        {
            if (classification.Contains("http", StringComparison.OrdinalIgnoreCase))
                ruleIdsToTrigger.Add("tls-cert-check");
            if (severity == "high")
                ruleIdsToTrigger.Add("exposed-service-check");
            if (classification is "rdp" or "vnc" or "telnet")
                ruleIdsToTrigger.Add("remote-access-check");
            if (classification is "redis" or "mongodb" or "elasticsearch" or "memcached")
                ruleIdsToTrigger.Add("unauthenticated-db-check");
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
        _logger.LogInformation($"Classified {obs.TargetId}:{obs.Port} as {classification} ({obs.State})");

        if (ruleIdsToTrigger.Any())
        {
            var stage2Job = new Stage2EnrichmentJob
            {
                ObservationId = obs.Id.ToString(),
                TargetIp = obs.TargetId,
                RuleIds = ruleIdsToTrigger
            };
            await _enrichmentQueue.EnqueueAsync(stage2Job, idempotencyKey: $"stage2:{obs.Id}");
            _logger.LogInformation($"Enqueued Stage 2 enrichment for {obs.TargetId}:{obs.Port} with rules: {string.Join(",", ruleIdsToTrigger)}");
        }
    }
}
