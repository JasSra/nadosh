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
            var serialized = JsonSerializer.Serialize(exposures);
            await db.StringSetAsync(cacheKey, serialized, TimeSpan.FromMinutes(5));
        });

        return Ok(exposures);
    }

    [HttpGet("{ip}/ports")]
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
        }

        var total = await query.CountAsync(ct);
        
        var results = await query
            .OrderByDescending(e => e.LastSeen)
            .Skip(request.Skip)
            .Take(request.Take)
            .ToListAsync(ct);

        return Ok(new
        {
            TotalCount = total,
            QueryParsed = request.Query ?? "legacy-mode",
            Results = results
        });
    }
}

public class ExposureSearchRequest
{
    /// <summary>
    /// Query DSL string. Example: "service:ssh AND port:22 AND time:last_7d"
    /// Supported fields: port, service, severity, state, protocol, classification, tier, time
    /// Boolean operators: AND, OR, NOT
    /// Time formats: last_7d, last_30d, last_1h, since:2026-01-01
    /// </summary>
    public string? Query { get; set; }
    
    // Legacy filters (deprecated - use Query DSL instead)
    public string? Ip { get; set; }
    public string? Severity { get; set; }
    public string? Classification { get; set; }
    public int? Port { get; set; }
    
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 50;
}
