using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nadosh.Core.Models;
using Nadosh.Core.Services;
using Nadosh.Infrastructure.Data;

namespace Nadosh.Workers;

/// <summary>
/// CVE Enrichment Worker - scans observations with detected service versions
/// and enriches them with known CVE vulnerabilities from the NVD database.
/// Runs periodically to keep vulnerability data up to date.
/// </summary>
public class CveEnrichmentWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CveEnrichmentWorker> _logger;
    private static readonly string WorkerId = $"{Environment.MachineName}/cve-enrichment/{Environment.ProcessId}";

    public CveEnrichmentWorker(IServiceProvider serviceProvider, ILogger<CveEnrichmentWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[{WorkerId}] CVE Enrichment Worker starting up...", WorkerId);
        
        // Wait 30 seconds for other services to initialize
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnrichObservationsWithCvesAsync(stoppingToken);
                
                // Run every 6 hours to check for new CVEs
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("[{WorkerId}] CVE Enrichment Worker shutting down gracefully", WorkerId);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{WorkerId}] Fatal error in CVE enrichment loop", WorkerId);
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    private async Task EnrichObservationsWithCvesAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NadoshDbContext>();
        var cveService = scope.ServiceProvider.GetRequiredService<CveEnrichmentService>();

        try
        {
            _logger.LogInformation("[{WorkerId}] Starting CVE enrichment cycle...", WorkerId);

            // Find observations with service versions that haven't been enriched with CVE data
            var cutoff = DateTime.UtcNow.AddDays(-7); // Re-check every 7 days
            
            var candidateObservations = await dbContext.Observations
                .Where(o => 
                    o.ServiceName != null && 
                    o.ServiceVersion != null &&
                    (o.Tier == ScanTier.BannerGrab || o.Tier == ScanTier.Fingerprint) &&
                    o.State == "open" &&
                    o.CveIds == null) // Only process observations that haven't been enriched yet
                .OrderBy(o => o.ObservedAt)
                .Take(100) // Process 100 at a time to respect rate limits
                .ToListAsync(ct);

            if (!candidateObservations.Any())
            {
                _logger.LogInformation("[{WorkerId}] No observations require CVE enrichment", WorkerId);
                
                // Also check CurrentExposures
                await EnrichCurrentExposuresAsync(dbContext, cveService, cutoff, ct);
                return;
            }

            _logger.LogInformation("[{WorkerId}] Found {Count} observations to enrich with CVE data", 
                WorkerId, candidateObservations.Count);

            var enrichedCount = 0;
            var cveFoundCount = 0;

            foreach (var observation in candidateObservations)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    // Search for CVEs related to this service
                    var cves = await cveService.SearchCvesAsync(
                        observation.ServiceName!,
                        observation.ProductVendor,
                        observation.ServiceVersion,
                        ct);

                    if (cves.Any())
                    {
                        // Store CVE information
                        var cveIds = string.Join(",", cves.Select(c => c.CveId));
                        var highestScore = cves.Max(c => c.CvssScore);
                        var highestSeverity = cves
                            .OrderByDescending(c => c.CvssScore)
                            .First()
                            .Severity;

                        observation.CveIds = cveIds;
                        observation.HighestCvssScore = highestScore;
                        observation.CveSeverity = highestSeverity;

                        _logger.LogInformation(
                            "[{WorkerId}] Found {CveCount} CVEs for {Service} {Version} on {Ip}:{Port} - Highest: {CveId} (CVSS: {Score})",
                            WorkerId, cves.Count, observation.ServiceName, observation.ServiceVersion,
                            observation.TargetId, observation.Port, cves.First().CveId, highestScore);

                        cveFoundCount++;
                    }
                    else
                    {
                        // Mark as checked even if no CVEs found
                        observation.CveIds = string.Empty;
                        observation.HighestCvssScore = null;
                        observation.CveSeverity = null;
                    }

                    enrichedCount++;

                    // Rate limiting: NVD API allows 5 requests per 30 seconds without API key
                    await Task.Delay(TimeSpan.FromSeconds(6), ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, 
                        "[{WorkerId}] Failed to enrich observation {Id} ({Service} {Version})",
                        WorkerId, observation.Id, observation.ServiceName, observation.ServiceVersion);
                }
            }

            await dbContext.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[{WorkerId}] CVE enrichment complete: {Enriched} observations processed, {Found} with CVEs found",
                WorkerId, enrichedCount, cveFoundCount);

            // Also enrich CurrentExposures table
            await EnrichCurrentExposuresAsync(dbContext, cveService, cutoff, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{WorkerId}] Error during CVE enrichment cycle", WorkerId);
        }
    }

    private async Task EnrichCurrentExposuresAsync(
        NadoshDbContext dbContext, 
        CveEnrichmentService cveService, 
        DateTime cutoff, 
        CancellationToken ct)
    {
        try
        {
            // Find exposures that need CVE enrichment by joining with latest observations
            var exposuresNeedingEnrichment = await (
                from exposure in dbContext.CurrentExposures
                join observation in dbContext.Observations on 
                    new { exposure.TargetId, exposure.Port, exposure.Protocol } equals 
                    new { observation.TargetId, observation.Port, observation.Protocol }
                where observation.ServiceName != null && 
                      observation.ServiceVersion != null &&
                      observation.State == "open" &&
                      (exposure.CveLastChecked == null || exposure.CveLastChecked < cutoff)
                orderby observation.ObservedAt descending
                select new { Exposure = exposure, Observation = observation }
            )
            .Take(50)
            .ToListAsync(ct);

            if (!exposuresNeedingEnrichment.Any())
            {
                _logger.LogInformation("[{WorkerId}] No current exposures require CVE enrichment", WorkerId);
                return;
            }

            _logger.LogInformation("[{WorkerId}] Found {Count} current exposures to enrich with CVE data",
                WorkerId, exposuresNeedingEnrichment.Count);

            var enrichedCount = 0;
            var cveFoundCount = 0;

            foreach (var item in exposuresNeedingEnrichment)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var observation = item.Observation;
                    var exposure = item.Exposure;

                    var cves = await cveService.SearchCvesAsync(
                        observation.ServiceName!,
                        observation.ProductVendor,
                        observation.ServiceVersion,
                        ct);

                    if (cves.Any())
                    {
                        var cveIds = string.Join(",", cves.Select(c => c.CveId));
                        var highestScore = cves.Max(c => c.CvssScore);
                        var highestSeverity = cves
                            .OrderByDescending(c => c.CvssScore)
                            .First()
                            .Severity;

                        exposure.CveIds = cveIds;
                        exposure.HighestCvssScore = highestScore;
                        exposure.CveSeverity = highestSeverity;
                        exposure.CveLastChecked = DateTime.UtcNow;

                        _logger.LogInformation(
                            "[{WorkerId}] Enriched exposure {Ip}:{Port} with {CveCount} CVEs - Highest CVSS: {Score}",
                            WorkerId, exposure.TargetId, exposure.Port, cves.Count, highestScore);

                        cveFoundCount++;
                    }
                    else
                    {
                        exposure.CveIds = string.Empty;
                        exposure.HighestCvssScore = null;
                        exposure.CveSeverity = null;
                        exposure.CveLastChecked = DateTime.UtcNow;
                    }

                    enrichedCount++;

                    // Rate limiting
                    await Task.Delay(TimeSpan.FromSeconds(6), ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[{WorkerId}] Failed to enrich exposure {Ip}:{Port}",
                        WorkerId, item.Exposure.TargetId, item.Exposure.Port);
                }
            }

            await dbContext.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[{WorkerId}] CurrentExposure CVE enrichment complete: {Enriched} processed, {Found} with CVEs",
                WorkerId, enrichedCount, cveFoundCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{WorkerId}] Error enriching CurrentExposures with CVE data", WorkerId);
        }
    }
}
