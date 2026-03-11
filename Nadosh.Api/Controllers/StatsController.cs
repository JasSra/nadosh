using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nadosh.Infrastructure.Data;
using Nadosh.Core.Models;
using Nadosh.Api.Infrastructure;
using StackExchange.Redis;
using System.Text.Json;

namespace Nadosh.Api.Controllers;

[ApiController]
[ApiKeyAuth]
[Route("v1/[controller]")]
public class StatsController : ControllerBase
{
    private readonly NadoshDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<StatsController> _logger;

    public StatsController(NadoshDbContext dbContext, IConnectionMultiplexer redis, ILogger<StatsController> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _logger = logger;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var cacheKey = "stats:summary";
        
        // Try Redis cache first
        var cached = await db.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            _logger.LogInformation("Stats summary cache hit");
            return Ok(JsonSerializer.Deserialize<object>(cached.ToString()));
        }

        // Compute from Observations (latest per target/port)
        var openPorts = await _dbContext.Observations
            .Where(o => o.State == "open")
            .GroupBy(o => new { o.TargetId, o.Port })
            .Select(g => g.OrderByDescending(x => x.ObservedAt).First())
            .ToListAsync(ct);

        var summary = new
        {
            totalHosts = openPorts.Select(o => o.TargetId).Distinct().Count(),
            totalOpenPorts = openPorts.Count,
            scanCoverage = await _dbContext.Targets.CountAsync(ct),
            topServices = openPorts
                .Where(o => !string.IsNullOrEmpty(o.ServiceName))
                .GroupBy(o => o.ServiceName)
                .Select(g => new { service = g.Key, count = g.Count() })
                .OrderByDescending(s => s.count)
                .Take(10)
                .ToList(),
            topPorts = openPorts
                .GroupBy(o => o.Port)
                .Select(g => new { port = g.Key, count = g.Count() })
                .OrderByDescending(p => p.count)
                .Take(10)
                .ToList(),
            timestamp = DateTime.UtcNow
        };

        // Cache for 60 seconds
        await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(summary), TimeSpan.FromSeconds(60));

        return Ok(summary);
    }

    [HttpGet("ports")]
    public async Task<IActionResult> GetTopPorts([FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var ports = await _dbContext.Observations
            .Where(o => o.State == "open")
            .GroupBy(o => new { o.TargetId, o.Port })
            .Select(g => g.OrderByDescending(x => x.ObservedAt).First())
            .GroupBy(o => o.Port)
            .Select(g => new { 
                port = g.Key, 
                hostCount = g.Count(),
                commonService = g.Select(x => x.ServiceName).FirstOrDefault()
            })
            .OrderByDescending(x => x.hostCount)
            .Take(limit)
            .ToListAsync(ct);

        return Ok(new { total = ports.Count, ports });
    }

    [HttpGet("services")]
    public async Task<IActionResult> GetServiceDistribution(CancellationToken ct)
    {
        var services = await _dbContext.Observations
            .Where(o => o.State == "open" && o.ServiceName != null)
            .GroupBy(o => new { o.TargetId, o.Port })
            .Select(g => g.OrderByDescending(x => x.ObservedAt).First())
            .GroupBy(o => o.ServiceName)
            .Select(g => new { 
                service = g.Key, 
                hostCount = g.Count(),
                ports = g.Select(x => x.Port).Distinct().OrderBy(p => p).ToList()
            })
            .OrderByDescending(x => x.hostCount)
            .ToListAsync(ct);

        return Ok(new { total = services.Count, services });
    }

    [HttpGet("severity")]
    public async Task<IActionResult> GetSeverityDistribution(CancellationToken ct)
    {
        // Use CurrentExposures if available, fall back to deriving from Observations
        var hasExposures = await _dbContext.CurrentExposures.AnyAsync(ct);
        
        if (hasExposures)
        {
            var severities = await _dbContext.CurrentExposures
                .AsNoTracking()
                .GroupBy(e => e.Severity)
                .Select(g => new { severity = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToListAsync(ct);

            return Ok(new { source = "CurrentExposures", severities });
        }
        else
        {
            // Derive severity from port numbers (simplified)
            var highRiskPorts = new[] { 22, 23, 3389, 5900, 3306, 5432, 6379, 27017 };
            
            var openObs = await _dbContext.Observations
                .Where(o => o.State == "open")
                .GroupBy(o => new { o.TargetId, o.Port })
                .Select(g => g.OrderByDescending(x => x.ObservedAt).First())
                .ToListAsync(ct);

            var severities = openObs
                .GroupBy(o => highRiskPorts.Contains(o.Port) ? "high" : "medium")
                .Select(g => new { severity = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToList();

            return Ok(new { source = "Observations", severities });
        }
    }

    [HttpGet("trends")]
    public async Task<IActionResult> GetTrends([FromQuery] int days = 14, [FromQuery] string granularity = "day", CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 90);
        var since = DateTime.UtcNow.AddDays(-days);

        // Count new exposures (FirstSeen) per day from CurrentExposures
        var newExposures = await _dbContext.CurrentExposures
            .AsNoTracking()
            .Where(e => e.FirstSeen >= since)
            .GroupBy(e => e.FirstSeen.Date)
            .Select(g => new { date = g.Key, count = g.Count() })
            .OrderBy(x => x.date)
            .ToListAsync(ct);

        // Count state changes per day
        var stateChanges = await _dbContext.CurrentExposures
            .AsNoTracking()
            .Where(e => e.LastChanged >= since)
            .GroupBy(e => e.LastChanged.Date)
            .Select(g => new { date = g.Key, count = g.Count() })
            .OrderBy(x => x.date)
            .ToListAsync(ct);

        // Total observations per day (as a proxy for scan activity)
        var scanActivity = await _dbContext.Observations
            .AsNoTracking()
            .Where(o => o.ObservedAt >= since)
            .GroupBy(o => o.ObservedAt.Date)
            .Select(g => new { date = g.Key, total = g.Count(), open = g.Count(x => x.State == "open") })
            .OrderBy(x => x.date)
            .ToListAsync(ct);

        return Ok(new
        {
            days,
            granularity,
            since,
            newExposures,
            stateChanges,
            scanActivity
        });
    }
    public async Task<IActionResult> GetSubnetStats(string cidr, CancellationToken ct)
    {
        // Simple prefix match for demo (e.g., "192.168.4")
        var openPorts = await _dbContext.Observations
            .Where(o => o.State == "open" && o.TargetId.StartsWith(cidr))
            .GroupBy(o => new { o.TargetId, o.Port })
            .Select(g => g.OrderByDescending(x => x.ObservedAt).First())
            .ToListAsync(ct);

        var stats = new
        {
            cidr,
            totalHosts = openPorts.Select(o => o.TargetId).Distinct().Count(),
            totalOpenPorts = openPorts.Count,
            services = openPorts
                .Where(o => !string.IsNullOrEmpty(o.ServiceName))
                .GroupBy(o => o.ServiceName)
                .Select(g => new { service = g.Key, count = g.Count() })
                .OrderByDescending(s => s.count)
                .ToList(),
            hosts = openPorts
                .GroupBy(o => o.TargetId)
                .Select(g => new
                {
                    ip = g.Key,
                    openPortCount = g.Count(),
                    ports = g.Select(x => x.Port).OrderBy(p => p).ToList(),
                    services = g.Where(x => !string.IsNullOrEmpty(x.ServiceName))
                        .Select(x => x.ServiceName).Distinct().ToList()
                })
                .OrderBy(h => h.ip)
                .ToList()
        };

        return Ok(stats);
    }
}
