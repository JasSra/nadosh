using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nadosh.Infrastructure.Data;
using StackExchange.Redis;
using System.Text.Json;

namespace Nadosh.Api.Controllers;

[ApiController]
[Route("v1/timeline")]
public class TimelineController : ControllerBase
{
    private readonly NadoshDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<TimelineController> _logger;

    public TimelineController(
        NadoshDbContext db,
        IConnectionMultiplexer redis,
        ILogger<TimelineController> logger)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Get full timeline of all observations for a specific IP
    /// Shows historical changes: new ports, closed ports, banner changes, version changes
    /// </summary>
    [HttpGet("{ip}")]
    public async Task<IActionResult> GetIpTimeline(
        string ip,
        [FromQuery] DateTime? since = null,
        [FromQuery] DateTime? until = null,
        [FromQuery] int? port = null,
        [FromQuery] bool changesOnly = false)
    {
        var cache = _redis.GetDatabase();
        var cacheKey = $"timeline:{ip}:{since?.ToString("yyyyMMddHHmm")}:{until?.ToString("yyyyMMddHHmm")}:{port}:{changesOnly}";

        // Try cache first (5 minute TTL since timeline is historical)
        var cached = await cache.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            _logger.LogInformation("Timeline cache hit for {Ip}", ip);
            return Content(cached.ToString(), "application/json");
        }

        // Build query
        var query = _db.Observations
            .AsNoTracking()
            .Where(o => o.TargetId == ip);

        if (since.HasValue)
            query = query.Where(o => o.ObservedAt >= since.Value);

        if (until.HasValue)
            query = query.Where(o => o.ObservedAt <= until.Value);

        if (port.HasValue)
            query = query.Where(o => o.Port == port.Value);

        var observations = await query
            .OrderBy(o => o.ObservedAt)
            .Select(o => new
            {
                o.ObservedAt,
                o.Port,
                o.Protocol,
                o.State,
                o.ServiceName,
                o.ServiceVersion,
                o.ProductVendor,
                o.Banner,
                o.HttpTitle,
                o.HttpServer,
                o.HttpStatusCode,
                o.SslSubject,
                o.SslIssuer,
                o.SslExpiry,
                o.JarmHash,
                o.ScanRunId,
                o.Tier
            })
            .ToListAsync();

        if (!observations.Any())
        {
            return NotFound(new { error = "No observations found for this IP" });
        }

        // Detect changes between consecutive observations
        var timeline = new List<object>();
        var previousByPort = new Dictionary<int, dynamic>();

        foreach (var obs in observations)
        {
            var changes = new List<string>();
            var isNewPort = !previousByPort.ContainsKey(obs.Port);

            if (isNewPort)
            {
                changes.Add("NEW_PORT");
            }
            else
            {
                var prev = previousByPort[obs.Port];

                if (obs.State != prev.State)
                    changes.Add($"STATE_CHANGE:{prev.State}→{obs.State}");

                if (obs.ServiceName != prev.ServiceName)
                    changes.Add($"SERVICE_CHANGE:{prev.ServiceName}→{obs.ServiceName}");

                if (obs.ServiceVersion != prev.ServiceVersion && !string.IsNullOrEmpty(obs.ServiceVersion))
                    changes.Add($"VERSION_CHANGE:{prev.ServiceVersion}→{obs.ServiceVersion}");

                if (obs.Banner != prev.Banner && !string.IsNullOrEmpty(obs.Banner))
                    changes.Add("BANNER_CHANGE");

                if (obs.HttpTitle != prev.HttpTitle && !string.IsNullOrEmpty(obs.HttpTitle))
                    changes.Add("HTTP_TITLE_CHANGE");

                if (obs.SslSubject != prev.SslSubject && !string.IsNullOrEmpty(obs.SslSubject))
                    changes.Add("CERTIFICATE_CHANGE");

                if (obs.JarmHash != prev.JarmHash && !string.IsNullOrEmpty(obs.JarmHash))
                    changes.Add("TLS_FINGERPRINT_CHANGE");

            }

            // Skip if changesOnly=true and no changes detected
            if (changesOnly && !changes.Any())
            {
                previousByPort[obs.Port] = obs;
                continue;
            }

            var entry = new
            {
                observedAt = obs.ObservedAt,
                port = obs.Port,
                protocol = obs.Protocol,
                state = obs.State,
                serviceName = obs.ServiceName,
                serviceVersion = obs.ServiceVersion,
                productVendor = obs.ProductVendor,
                banner = obs.Banner?.Length > 200 ? obs.Banner[..200] + "..." : obs.Banner,
                httpTitle = obs.HttpTitle,
                httpServer = obs.HttpServer,
                httpStatusCode = obs.HttpStatusCode,
                sslSubject = obs.SslSubject,
                sslIssuer = obs.SslIssuer,
                sslExpiry = obs.SslExpiry,
                jarmHash = obs.JarmHash,
                scanRunId = obs.ScanRunId,
                tier = obs.Tier,
                changes = changes,
                isFirstSeen = isNewPort
            };

            timeline.Add(entry);
            previousByPort[obs.Port] = obs;
        }

        // Build summary statistics
        var firstSeen = observations.Min(o => o.ObservedAt);
        var lastSeen = observations.Max(o => o.ObservedAt);
        var uniquePorts = observations.Select(o => o.Port).Distinct().Count();
        var totalObservations = observations.Count;
        var uniqueServices = observations.Where(o => !string.IsNullOrEmpty(o.ServiceName))
            .Select(o => o.ServiceName)
            .Distinct()
            .Count();

        var result = new
        {
            ip,
            summary = new
            {
                firstSeen,
                lastSeen,
                totalObservations,
                uniquePorts,
                uniqueServices,
                scanRuns = observations.Select(o => o.ScanRunId).Distinct().Count(),
                timelineSpan = lastSeen - firstSeen
            },
            timeline
        };

        var json = JsonSerializer.Serialize(result);
        await cache.StringSetAsync(cacheKey, json, TimeSpan.FromMinutes(5));

        return Content(json, "application/json");
    }

    /// <summary>
    /// Get change summary across all IPs - what changed in the last N days
    /// </summary>
    [HttpGet("changes")]
    public async Task<IActionResult> GetRecentChanges(
        [FromQuery] int days = 7,
        [FromQuery] string? changeType = null)
    {
        var since = DateTime.UtcNow.AddDays(-days);

        var cache = _redis.GetDatabase();
        var cacheKey = $"timeline:changes:{days}:{changeType}";

        var cached = await cache.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            return Content(cached.ToString(), "application/json");
        }

        // Get all observations in time window grouped by IP+Port
        var observations = await _db.Observations
            .AsNoTracking()
            .Where(o => o.ObservedAt >= since)
            .OrderBy(o => o.TargetId)
            .ThenBy(o => o.Port)
            .ThenBy(o => o.ObservedAt)
            .Select(o => new
            {
                Ip = o.TargetId,
                o.Port,
                o.ObservedAt,
                o.State,
                o.ServiceName,
                o.ServiceVersion,
                o.Banner
            })
            .ToListAsync();

        var changes = new List<object>();
        var groupedByIpPort = observations.GroupBy(o => new { o.Ip, o.Port });

        foreach (var group in groupedByIpPort)
        {
            var ordered = group.OrderBy(o => o.ObservedAt).ToList();
            if (ordered.Count < 2) continue;

            for (int i = 1; i < ordered.Count; i++)
            {
                var prev = ordered[i - 1];
                var curr = ordered[i];

                var detected = new List<string>();

                if (curr.State != prev.State)
                    detected.Add($"STATE:{prev.State}→{curr.State}");

                if (curr.ServiceName != prev.ServiceName)
                    detected.Add($"SERVICE:{prev.ServiceName}→{curr.ServiceName}");

                if (curr.ServiceVersion != prev.ServiceVersion && !string.IsNullOrEmpty(curr.ServiceVersion))
                    detected.Add($"VERSION:{prev.ServiceVersion}→{curr.ServiceVersion}");

                if (curr.Banner != prev.Banner && !string.IsNullOrEmpty(curr.Banner))
                    detected.Add("BANNER_CHANGED");

                if (detected.Any())
                {
                    changes.Add(new
                    {
                        ip = group.Key.Ip,
                        port = group.Key.Port,
                        detectedAt = curr.ObservedAt,
                        changes = detected,
                        previousState = new
                        {
                            observedAt = prev.ObservedAt,
                            state = prev.State,
                            service = prev.ServiceName,
                            version = prev.ServiceVersion
                        },
                        currentState = new
                        {
                            observedAt = curr.ObservedAt,
                            state = curr.State,
                            service = curr.ServiceName,
                            version = curr.ServiceVersion
                        }
                    });
                }
            }
        }

        // Filter by change type if requested
        if (!string.IsNullOrEmpty(changeType))
        {
            changes = changes.Where(c =>
            {
                var changeList = (List<string>)((dynamic)c).changes;
                return changeList.Any(ch => ch.StartsWith(changeType, StringComparison.OrdinalIgnoreCase));
            }).ToList();
        }

        var result = new
        {
            periodDays = days,
            since,
            totalChangesDetected = changes.Count,
            filterByChangeType = changeType,
            changes = changes.OrderByDescending(c => ((dynamic)c).detectedAt).Take(100)
        };

        var json = JsonSerializer.Serialize(result);
        await cache.StringSetAsync(cacheKey, json, TimeSpan.FromMinutes(2));

        return Content(json, "application/json");
    }

    /// <summary>
    /// Get service lifecycle - when was a service first/last seen, uptime %
    /// </summary>
    [HttpGet("services/{service}")]
    public async Task<IActionResult> GetServiceLifecycle(string service)
    {
        var observations = await _db.Observations
            .AsNoTracking()
            .Where(o => o.ServiceName == service)
            .GroupBy(o => new { Ip = o.TargetId, o.Port })
            .Select(g => new
            {
                g.Key.Ip,
                g.Key.Port,
                FirstSeen = g.Min(o => o.ObservedAt),
                LastSeen = g.Max(o => o.ObservedAt),
                ObservationCount = g.Count(),
                Versions = g.Where(o => !string.IsNullOrEmpty(o.ServiceVersion))
                    .Select(o => o.ServiceVersion)
                    .Distinct()
                    .ToList()
            })
            .ToListAsync();

        if (!observations.Any())
        {
            return NotFound(new { error = $"No observations found for service: {service}" });
        }

        var result = new
        {
            service,
            totalHosts = observations.Count,
            firstSeenGlobal = observations.Min(o => o.FirstSeen),
            lastSeenGlobal = observations.Max(o => o.LastSeen),
            hosts = observations.OrderByDescending(o => o.LastSeen).Take(50)
        };

        return Ok(result);
    }
}
