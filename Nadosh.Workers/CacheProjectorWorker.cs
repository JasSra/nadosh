using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Nadosh.Infrastructure.Data;
using Nadosh.Core.Models;
using StackExchange.Redis;
using System.Text.Json;

namespace Nadosh.Workers;

/// <summary>
/// Periodically projects Observations → CurrentExposures table and updates Redis cache
/// This ensures the hot path (CurrentExposures + Redis) stays in sync with the source of truth (Observations)
/// </summary>
public class CacheProjectorWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<CacheProjectorWorker> _logger;

    public CacheProjectorWorker(IServiceProvider services, IConnectionMultiplexer redis, ILogger<CacheProjectorWorker> logger)
    {
        _services = services;
        _redis = redis;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CacheProjectorWorker starting — will project Observations → CurrentExposures every 60s");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProjectCurrentExposuresAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CacheProjectorWorker");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task ProjectCurrentExposuresAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NadoshDbContext>();
        var redisDb = _redis.GetDatabase();

        // Get all latest observations per target/port
        var latestObservations = await db.Observations
            .Where(o => o.State == "open")
            .GroupBy(o => new { o.TargetId, o.Port })
            .Select(g => g.OrderByDescending(x => x.ObservedAt).First())
            .ToListAsync(ct);

        _logger.LogInformation($"Projecting {latestObservations.Count} open port observations → CurrentExposures");

        // Get latest MAC addresses from Targets
        var targets = await db.Targets
            .Where(t => t.MacAddress != null)
            .Select(t => new { t.Ip, t.MacAddress, t.MacVendor, t.DeviceType })
            .ToListAsync(ct);
        var macLookup = targets.ToDictionary(t => t.Ip);

        var exposuresToUpsert = latestObservations.Select(obs => new CurrentExposure
        {
            TargetId = obs.TargetId,
            Port = obs.Port,
            Protocol = obs.Protocol,
            CurrentState = obs.State,
            FirstSeen = obs.ObservedAt, // Simplified — should track actual first seen
            LastSeen = obs.ObservedAt,
            LastChanged = obs.ObservedAt,
            Classification = obs.ServiceName ?? DeriveClassification(obs.Port),
            Severity = DeriveSeverity(obs.Port, obs.ServiceName),
            CachedSummary = BuildSummary(obs),
            MacAddress = macLookup.TryGetValue(obs.TargetId, out var mac) ? mac.MacAddress : obs.MacAddress,
            MacVendor = macLookup.TryGetValue(obs.TargetId, out var mac2) ? mac2.MacVendor : obs.MacVendor,
            DeviceType = macLookup.TryGetValue(obs.TargetId, out var mac3) ? mac3.DeviceType : obs.DeviceType
        }).ToList();

        // Bulk upsert: delete all and re-insert (simpler for MVP, can optimize later with ON CONFLICT)
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"CurrentExposures\"", ct);
        db.CurrentExposures.AddRange(exposuresToUpsert);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation($"Upserted {exposuresToUpsert.Count} records into CurrentExposures");

        // Update Redis cache per IP
        var groupedByIp = exposuresToUpsert.GroupBy(e => e.TargetId);
        foreach (var ipGroup in groupedByIp)
        {
            var cacheKey = $"exposure:current:{ipGroup.Key}";
            var serialized = JsonSerializer.Serialize(ipGroup.ToList());
            await redisDb.StringSetAsync(cacheKey, serialized, TimeSpan.FromMinutes(5));
        }

        _logger.LogInformation($"Updated Redis cache for {groupedByIp.Count()} IPs");
    }

    private string DeriveClassification(int port)
    {
        return port switch
        {
            22 => "ssh",
            80 => "http",
            443 => "https",
            3306 => "mysql",
            5432 => "postgresql",
            6379 => "redis",
            _ => "unknown"
        };
    }

    private string DeriveSeverity(int port, string? serviceName)
    {
        var highRiskPorts = new[] { 22, 23, 3389, 5900, 3306, 5432, 6379, 27017 };
        if (highRiskPorts.Contains(port)) return "high";
        if (serviceName == "http" || serviceName == "https") return "medium";
        return "low";
    }

    private string BuildSummary(Observation obs)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(obs.ServiceName)) parts.Add($"Service: {obs.ServiceName}");
        if (!string.IsNullOrEmpty(obs.Banner)) parts.Add($"Banner: {obs.Banner}");
        if (!string.IsNullOrEmpty(obs.HttpTitle)) parts.Add($"Title: {obs.HttpTitle}");
        if (!string.IsNullOrEmpty(obs.HttpServer)) parts.Add($"Server: {obs.HttpServer}");
        if (!string.IsNullOrEmpty(obs.SslSubject)) parts.Add($"TLS: {obs.SslSubject}");
        return string.Join(" | ", parts);
    }
}
