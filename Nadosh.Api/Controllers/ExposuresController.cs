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
public class ExposuresController : ControllerBase
{
    private readonly NadoshDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ExposuresController> _logger;

    public ExposuresController(NadoshDbContext dbContext, IConnectionMultiplexer redis, ILogger<ExposuresController> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _logger = logger;
    }

    [HttpGet("{ip}")]
    public async Task<IActionResult> GetExposureByIp(string ip, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var cacheKey = $"exposure:current:{ip}";

        // Fast path: Redis (Simulated for now unless we enforce cache writing in previous steps)
        var cached = await db.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            _logger.LogInformation($"Cache hit for IP {ip}");
            var cachedExposures = JsonSerializer.Deserialize<List<CurrentExposure>>(cached.ToString());
            return Ok(cachedExposures);
        }

        // Slow path: PostgreSQL
        _logger.LogInformation($"Cache miss for IP {ip}, hitting PG.");
        var exposures = await _dbContext.CurrentExposures
            .AsNoTracking()
            .Where(e => e.TargetId == ip)
            .ToListAsync(ct);

        if (!exposures.Any())
        {
            return NotFound(new { Message = $"No exposures found for IP {ip}" });
        }

        // Async Cache population
        _ = Task.Run(async () =>
        {
            try
            {
                var serialized = JsonSerializer.Serialize(exposures);
                await db.StringSetAsync(cacheKey, serialized, TimeSpan.FromMinutes(5));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to populate cache for IP {Ip}", ip);
            }
        });

        return Ok(exposures);
    }

    [HttpPost("batch")]
    public async Task<IActionResult> GetExposuresBatch([FromBody] BatchIpRequest request, CancellationToken ct)
    {
        if (request.Ips == null || request.Ips.Count == 0)
            return BadRequest(new { Error = "At least one IP is required." });

        if (request.Ips.Count > 100)
            return BadRequest(new { Error = "Maximum 100 IPs per batch request." });

        var db = _redis.GetDatabase();
        var results = new List<object>();

        // Pipeline Redis reads
        var tasks = request.Ips.Select(async ip =>
        {
            var cacheKey = $"exposure:current:{ip}";
            var cached = await db.StringGetAsync(cacheKey);
            if (cached.HasValue)
            {
                return (ip, exposures: JsonSerializer.Deserialize<List<CurrentExposure>>(cached.ToString()), fromCache: true);
            }
            return (ip, exposures: (List<CurrentExposure>?)null, fromCache: false);
        }).ToList();

        var cacheResults = await Task.WhenAll(tasks);

        var missedIps = cacheResults.Where(r => !r.fromCache).Select(r => r.ip).ToList();
        Dictionary<string, List<CurrentExposure>> dbResults = new();

        if (missedIps.Count > 0)
        {
            var dbExposures = await _dbContext.CurrentExposures
                .AsNoTracking()
                .Where(e => missedIps.Contains(e.TargetId))
                .ToListAsync(ct);

            dbResults = dbExposures
                .GroupBy(e => e.TargetId)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        foreach (var ip in request.Ips)
        {
            var cacheEntry = cacheResults.FirstOrDefault(r => r.ip == ip);
            if (cacheEntry.fromCache && cacheEntry.exposures != null)
            {
                results.Add(new { ip, exposures = cacheEntry.exposures, source = "cache" });
            }
            else if (dbResults.TryGetValue(ip, out var dbExposures))
            {
                results.Add(new { ip, exposures = dbExposures, source = "db" });
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var serialized = JsonSerializer.Serialize(dbExposures);
                        await db.StringSetAsync($"exposure:current:{ip}", serialized, TimeSpan.FromMinutes(5));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to populate cache for IP {Ip}", ip);
                    }
                });
            }
            else
            {
                results.Add(new { ip, exposures = Array.Empty<object>(), source = "not-found" });
            }
        }

        return Ok(new { count = results.Count, results });
    }

    [HttpGet("{ip}/enrichments")]
    public async Task<IActionResult> GetEnrichmentsByIp(string ip, CancellationToken ct)
    {
        var observationIds = await _dbContext.Observations
            .AsNoTracking()
            .Where(o => o.TargetId == ip)
            .Select(o => o.Id)
            .ToListAsync(ct);

        if (observationIds.Count == 0)
            return NotFound(new { Message = $"No observations found for IP {ip}" });

        var enrichments = await _dbContext.EnrichmentResults
            .AsNoTracking()
            .Where(e => e.ObservationId.HasValue && observationIds.Contains(e.ObservationId!.Value))
            .OrderByDescending(e => e.ExecutedAt)
            .ToListAsync(ct);

        return Ok(new { ip, count = enrichments.Count, enrichments });
    }
    public async Task<IActionResult> GetPortsByIp(string ip, CancellationToken ct)
    {
        var exposures = await _dbContext.CurrentExposures
            .AsNoTracking()
            .Where(e => e.TargetId == ip)
            .Select(e => new { e.Port, e.Protocol, e.CurrentState, e.Classification, e.Severity, e.FirstSeen, e.LastSeen, e.LastChanged })
            .ToListAsync(ct);

        if (!exposures.Any()) return NotFound();

        return Ok(exposures);
    }

    [HttpPost("search")]
    public async Task<IActionResult> SearchExposures([FromBody] ExposureSearchRequest request, CancellationToken ct)
    {
        var take = Math.Clamp(request.Take, 1, 500);
        var query = _dbContext.CurrentExposures.AsNoTracking().AsQueryable();

        // Apply query DSL if provided
        if (!string.IsNullOrEmpty(request.Query))
        {
            try
            {
                var dslFilter = QueryDslParser.Parse(request.Query);
                query = query.Where(dslFilter);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = "Invalid query syntax", Message = ex.Message, Example = "service:ssh AND port:22 AND time:last_7d" });
            }
        }
        else
        {
            // Legacy field-based filtering
            if (!string.IsNullOrEmpty(request.Ip))
                query = query.Where(e => e.TargetId == request.Ip);
                
            if (!string.IsNullOrEmpty(request.Severity))
                query = query.Where(e => e.Severity == request.Severity);
                
            if (!string.IsNullOrEmpty(request.Classification))
                query = query.Where(e => e.Classification == request.Classification);

            if (request.Port.HasValue)
                query = query.Where(e => e.Port == request.Port.Value);
            
            if (!string.IsNullOrEmpty(request.ThreatLevel))
                query = query.Where(e => e.ThreatLevel == request.ThreatLevel);
            
            if (request.MinThreatScore.HasValue)
                query = query.Where(e => e.ThreatScore >= request.MinThreatScore.Value);
        }

        // Apply cursor-based pagination
        if (!string.IsNullOrEmpty(request.After))
        {
            if (long.TryParse(Base64UrlDecode(request.After), out var cursorId))
            {
                query = query.Where(e => e.Id < cursorId);
            }
        }

        var results = await query
            .OrderByDescending(e => e.LastSeen)
            .ThenByDescending(e => e.Id)
            .Take(take + 1)
            .ToListAsync(ct);

        var hasNextPage = results.Count > take;
        if (hasNextPage) results = results.Take(take).ToList();

        var endCursor = results.Count > 0 ? Base64UrlEncode(results[^1].Id.ToString()) : null;

        return Ok(new
        {
            QueryParsed = request.Query ?? "legacy-mode",
            PageInfo = new { HasNextPage = hasNextPage, EndCursor = endCursor },
            Results = results
        });
    }

    private static string Base64UrlEncode(string value)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? Base64UrlDecode(string value)
    {
        try
        {
            value = value.Replace('-', '+').Replace('_', '/');
            var padding = 4 - (value.Length % 4);
            if (padding < 4) value += new string('=', padding);
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch { return null; }
    }
}

public class BatchIpRequest
{
    public List<string> Ips { get; set; } = new();
}

public class ExposureSearchRequest
{
    /// <summary>
    /// Query DSL string. Example: "service:ssh AND port:22 AND time:last_7d"
    /// Supported fields: ip, target, port, service, classification, severity, state, protocol,
    /// mac, macAddress, macVendor, deviceType, summary, time
    /// Boolean operators: AND, OR, NOT
    /// Time formats: last_7d, last_30d, last_1h, since:2026-01-01
    /// </summary>
    public string? Query { get; set; }
    
    // Legacy filters (deprecated - use Query DSL instead)
    public string? Ip { get; set; }
    public string? Severity { get; set; }
    public string? Classification { get; set; }
    public int? Port { get; set; }
    
    /// <summary>Filter by threat level: critical, high, medium, low, minimal</summary>
    public string? ThreatLevel { get; set; }
    
    /// <summary>Filter by minimum threat score (0-100)</summary>
    public double? MinThreatScore { get; set; }
    
    /// <summary>Opaque cursor from a previous page's PageInfo.EndCursor for cursor-based pagination.</summary>
    public string? After { get; set; }
    public int Take { get; set; } = 50;
}
