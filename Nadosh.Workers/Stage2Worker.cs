using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;
using Nadosh.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Nadosh.Workers;

public class Stage2Worker : BackgroundService
{
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

        while (!stoppingToken.IsCancellationRequested)
        {
            var msg = await queue.DequeueAsync(TimeSpan.FromMinutes(5), stoppingToken);
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
                _logger.LogError(ex, "Error processing Stage 2 enrichment job");
                await queue.DeadLetterAsync(msg, ex.Message, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(Stage2EnrichmentJob job, IServiceProvider scopedProvider, CancellationToken ct)
    {
        _logger.LogInformation($"Running enrichments for {job.TargetIp} - Rules: {string.Join(", ", job.RuleIds)}");

        var db = scopedProvider.GetRequiredService<NadoshDbContext>();
        
        foreach (var ruleId in job.RuleIds)
        {
            // Here we would lookup the actual rule from the RuleConfig registry
            // For MVP, we mock the outcome.
            
            var result = new EnrichmentResult
            {
                ObservationId = long.Parse(job.ObservationId),
                RuleId = ruleId,
                RuleVersion = "1.0",
                ExecutedAt = DateTime.UtcNow,
                ResultStatus = "success",
                Confidence = 0.9f,
                Summary = $"Mock result for {ruleId} on {job.TargetIp}",
                EvidenceJson = "{}",
                Tags = new string[] { "enriched" }
            };

            db.EnrichmentResults.Add(result);
            _logger.LogInformation($"Enrichment {ruleId} on {job.TargetIp} completed.");
        }

        await db.SaveChangesAsync(ct);
    }
}
