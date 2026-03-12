using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nadosh.Core.Services;
using Nadosh.Infrastructure.Data;

namespace Nadosh.Workers;

/// <summary>
/// Threat Scoring Worker - calculates ML-based risk scores for all current exposures.
/// Runs periodically to update threat intelligence with latest risk factors.
/// Integrates CVE data, service types, exposure duration, and MITRE ATT&CK mappings.
/// </summary>
public class ThreatScoringWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ThreatScoringWorker> _logger;
    private static readonly string WorkerId = $"{Environment.MachineName}/threat-scoring/{Environment.ProcessId}";

    public ThreatScoringWorker(IServiceProvider serviceProvider, ILogger<ThreatScoringWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[{WorkerId}] Threat Scoring Worker starting up...", WorkerId);
        
        // Wait 60 seconds for other services to initialize
        await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CalculateThreatScoresAsync(stoppingToken);
                
                // Run every 1 hour to keep threat intelligence current
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("[{WorkerId}] Threat Scoring Worker shutting down gracefully", WorkerId);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{WorkerId}] Fatal error in threat scoring loop", WorkerId);
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    private async Task CalculateThreatScoresAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NadoshDbContext>();
        var threatService = scope.ServiceProvider.GetRequiredService<ThreatScoringService>();
        var mitreService = scope.ServiceProvider.GetRequiredService<MitreAttackMappingService>();

        try
        {
            _logger.LogInformation("[{WorkerId}] Starting threat score calculation cycle...", WorkerId);

            // Get all current exposures that need scoring (or re-scoring)
            var cutoff = DateTime.UtcNow.AddHours(-2); // Re-score every 2 hours
            
            var exposures = await dbContext.CurrentExposures
                .Where(e => 
                    e.CurrentState == "open" && 
                    (e.ThreatScoreCalculatedAt == null || e.ThreatScoreCalculatedAt < cutoff))
                .Take(500) // Process 500 at a time
                .ToListAsync(ct);

            if (!exposures.Any())
            {
                _logger.LogInformation("[{WorkerId}] No exposures require threat scoring", WorkerId);
                return;
            }

            _logger.LogInformation("[{WorkerId}] Calculating threat scores for {Count} exposures", 
                WorkerId, exposures.Count);

            // Load associated targets for geolocation data
            var targetIps = exposures.Select(e => e.TargetId).Distinct().ToList();
            var targets = await dbContext.Targets
                .Where(t => targetIps.Contains(t.Ip))
                .ToDictionaryAsync(t => t.Ip, t => t, ct);

            var scoredCount = 0;
            var criticalCount = 0;
            var highCount = 0;

            foreach (var exposure in exposures)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    // Get target for geolocation
                    targets.TryGetValue(exposure.TargetId, out var target);

                    // Calculate threat score
                    var threatScore = threatService.CalculateThreatScore(exposure, target);

                    // Map to MITRE ATT&CK
                    var mitreMapping = mitreService.MapExposureToMitre(exposure);

                    // Update exposure
                    exposure.ThreatScore = threatScore.Score;
                    exposure.ThreatLevel = threatScore.Severity;
                    exposure.ThreatExplanation = threatScore.Explanation;
                    exposure.ThreatScoreCalculatedAt = DateTime.UtcNow;
                    exposure.MitreTactics = mitreMapping.GetTacticsString();
                    exposure.MitreTechniques = mitreMapping.GetTechniquesString();

                    if (threatScore.Severity == "critical")
                        criticalCount++;
                    else if (threatScore.Severity == "high")
                        highCount++;

                    scoredCount++;

                    if (scoredCount % 100 == 0)
                    {
                        _logger.LogInformation("[{WorkerId}] Progress: {Scored}/{Total} exposures scored",
                            WorkerId, scoredCount, exposures.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, 
                        "[{WorkerId}] Failed to score exposure {Ip}:{Port}",
                        WorkerId, exposure.TargetId, exposure.Port);
                }
            }

            await dbContext.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[{WorkerId}] Threat scoring complete: {Scored} exposures scored ({Critical} critical, {High} high)",
                WorkerId, scoredCount, criticalCount, highCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{WorkerId}] Error during threat score calculation", WorkerId);
        }
    }
}
