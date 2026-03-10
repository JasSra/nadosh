using System.Diagnostics;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;
using Nadosh.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Nadosh.Workers;

/// <summary>
/// MAC address enrichment worker.
/// Dequeues MAC addresses, looks up vendor information from IEEE OUI database,
/// and enriches both the Target and recent Observations with vendor/device type data.
/// </summary>
public class MacEnrichmentWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MacEnrichmentWorker> _logger;
    private readonly IMacVendorLookup _macVendorLookup;

    public MacEnrichmentWorker(
        IServiceProvider serviceProvider,
        ILogger<MacEnrichmentWorker> logger,
        IMacVendorLookup macVendorLookup)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _macVendorLookup = macVendorLookup;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MacEnrichmentWorker starting...");

        using var scope = _serviceProvider.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue<MacEnrichmentJob>>();

        while (!stoppingToken.IsCancellationRequested)
        {
            var msg = await queue.DequeueAsync(TimeSpan.FromMinutes(5), stoppingToken);
            if (msg == null)
            {
                await Task.Delay(1000, stoppingToken);
                continue;
            }

            try
            {
                await ProcessJobAsync(msg.Payload, scope.ServiceProvider, stoppingToken);
                await queue.AcknowledgeAsync(msg, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enriching MAC address {Mac} for {TargetIp}",
                    msg.Payload.MacAddress, msg.Payload.TargetIp);
                
                if (msg.AttemptCount >= 3)
                    await queue.DeadLetterAsync(msg, ex.Message, stoppingToken);
                else
                    await queue.RejectAsync(msg, reenqueue: true, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(MacEnrichmentJob job, IServiceProvider scopedProvider, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        _logger.LogInformation("Enriching MAC {Mac} for {TargetIp}", job.MacAddress, job.TargetIp);

        // Lookup vendor information
        var vendorInfo = _macVendorLookup.Lookup(job.MacAddress);
        
        if (vendorInfo == null)
        {
            _logger.LogWarning("No vendor found for MAC {Mac} (target {TargetIp})", 
                job.MacAddress, job.TargetIp);
            return;
        }

        var db = scopedProvider.GetRequiredService<NadoshDbContext>();

        // Update Target record
        var target = await db.Targets.FirstOrDefaultAsync(t => t.Ip == job.TargetIp, ct);
        if (target != null)
        {
            target.MacAddress = job.MacAddress;
            target.MacVendor = vendorInfo.Vendor;
            target.DeviceType = vendorInfo.DeviceType;
            target.MacEnrichmentCompletedAt = DateTime.UtcNow;
            
            _logger.LogInformation("Enriched target {TargetIp}: MAC={Mac}, Vendor={Vendor}, DeviceType={DeviceType}",
                job.TargetIp, job.MacAddress, vendorInfo.Vendor, vendorInfo.DeviceType ?? "unknown");
        }

        // Update recent Observations for this target
        var cutoff = DateTime.UtcNow.AddDays(-7); // Last 7 days
        var recentObservations = await db.Observations
            .Where(o => o.TargetId == job.TargetIp && o.ObservedAt >= cutoff)
            .ToListAsync(ct);

        foreach (var obs in recentObservations)
        {
            obs.MacAddress = job.MacAddress;
            obs.MacVendor = vendorInfo.Vendor;
            obs.DeviceType = vendorInfo.DeviceType;
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation("MAC enrichment complete for {TargetIp} — updated {ObsCount} observations in {Ms}ms",
            job.TargetIp, recentObservations.Count, sw.ElapsedMilliseconds);
    }
}
