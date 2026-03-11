using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nadosh.Infrastructure.Data;
using Nadosh.Core.Models;
using Nadosh.Api.Infrastructure;

namespace Nadosh.Api.Controllers;

[ApiController]
[ApiKeyAuth]
[Route("v1/[controller]")]
public class HistoryController : ControllerBase
{
    private readonly NadoshDbContext _dbContext;

    public HistoryController(NadoshDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("{ip}")]
    public async Task<IActionResult> GetHistory(string ip, [FromQuery] int? port, [FromQuery] DateTime? since, [FromQuery] string? after, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 500);
        var query = _dbContext.Observations
            .AsNoTracking()
            .Where(o => o.TargetId == ip);

        if (port.HasValue)
            query = query.Where(o => o.Port == port.Value);

        if (since.HasValue)
        {
            query = query.Where(o => o.ObservedAt >= since.Value);
        }
        else
        {
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
            query = query.Where(o => o.ObservedAt >= thirtyDaysAgo);
        }

        // Cursor-based pagination
        if (!string.IsNullOrEmpty(after) && long.TryParse(Base64UrlDecode(after), out var cursorId))
            query = query.Where(o => o.Id < cursorId);

        var results = await query
            .OrderByDescending(o => o.ObservedAt)
            .ThenByDescending(o => o.Id)
            .Take(take + 1)
            .ToListAsync(ct);

        var hasNextPage = results.Count > take;
        if (hasNextPage) results = results.Take(take).ToList();
        var endCursor = results.Count > 0 ? Base64UrlEncode(results[^1].Id.ToString()) : null;

        return Ok(new { pageInfo = new { hasNextPage, endCursor }, results });
    }

    [HttpGet("{ip}/changes")]
    public async Task<IActionResult> GetChanges(string ip, [FromQuery] int? port, [FromQuery] DateTime? since, CancellationToken ct = default)
    {
        var sinceDate = since ?? DateTime.UtcNow.AddDays(-30);
        var query = _dbContext.Observations
            .AsNoTracking()
            .Where(o => o.TargetId == ip && o.ObservedAt >= sinceDate);

        if (port.HasValue)
            query = query.Where(o => o.Port == port.Value);

        var allObs = await query
            .OrderBy(o => o.Port)
            .ThenBy(o => o.ObservedAt)
            .ToListAsync(ct);

        // Detect changes by comparing consecutive observations for same port
        var changes = new List<object>();
        var grouped = allObs.GroupBy(o => o.Port);
        foreach (var group in grouped)
        {
            var ordered = group.ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                var prev = ordered[i - 1];
                var curr = ordered[i];
                var changeTypes = new List<string>();

                if (prev.State != curr.State)
                    changeTypes.Add($"STATE_CHANGE:{prev.State}->{curr.State}");
                if (prev.ServiceName != curr.ServiceName)
                    changeTypes.Add($"SERVICE_CHANGE:{prev.ServiceName}->{curr.ServiceName}");
                if (prev.ServiceVersion != curr.ServiceVersion)
                    changeTypes.Add($"VERSION_CHANGE:{prev.ServiceVersion}->{curr.ServiceVersion}");
                if (prev.Banner != curr.Banner && curr.Banner != null)
                    changeTypes.Add("BANNER_CHANGE");

                if (changeTypes.Count > 0)
                {
                    changes.Add(new
                    {
                        port = curr.Port,
                        detectedAt = curr.ObservedAt,
                        changeTypes,
                        previousObservationId = prev.Id,
                        currentObservationId = curr.Id
                    });
                }
            }
        }

        return Ok(new { ip, changeCount = changes.Count, changes });
    }

    [HttpPost("search")]
    public async Task<IActionResult> SearchHistory([FromBody] HistorySearchRequest request, CancellationToken ct)
    {
        var take = Math.Clamp(request.Take, 1, 500);

        // Enforce mandatory time range to allow partition pruning
        var since = request.Since ?? DateTime.UtcNow.AddDays(-(request.DaysBack ?? 30));
        var until = request.Until ?? DateTime.UtcNow;

        var query = _dbContext.Observations
            .AsNoTracking()
            .Where(o => o.ObservedAt >= since && o.ObservedAt <= until);

        if (!string.IsNullOrEmpty(request.Ip))
            query = query.Where(o => o.TargetId == request.Ip);

        if (request.Port.HasValue)
            query = query.Where(o => o.Port == request.Port.Value);

        if (!string.IsNullOrEmpty(request.State))
            query = query.Where(o => o.State == request.State);

        if (!string.IsNullOrEmpty(request.ServiceName))
            query = query.Where(o => o.ServiceName == request.ServiceName);

        if (!string.IsNullOrEmpty(request.ScanRunId))
            query = query.Where(o => o.ScanRunId == request.ScanRunId);

        // Cursor-based pagination
        if (!string.IsNullOrEmpty(request.After) && long.TryParse(Base64UrlDecode(request.After), out var cursorId))
            query = query.Where(o => o.Id < cursorId);

        var results = await query
            .OrderByDescending(o => o.ObservedAt)
            .ThenByDescending(o => o.Id)
            .Take(take + 1)
            .ToListAsync(ct);

        var hasNextPage = results.Count > take;
        if (hasNextPage) results = results.Take(take).ToList();
        var endCursor = results.Count > 0 ? Base64UrlEncode(results[^1].Id.ToString()) : null;

        return Ok(new
        {
            pageInfo = new { hasNextPage, endCursor },
            timeRange = new { since, until },
            results
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

public class HistorySearchRequest
{
    public string? Ip { get; set; }
    public int? Port { get; set; }
    public string? State { get; set; }
    public string? ServiceName { get; set; }
    public string? ScanRunId { get; set; }
    public DateTime? Since { get; set; }
    public DateTime? Until { get; set; }
    /// <summary>Number of days back from now to search (default: 30). Used when Since is not provided.</summary>
    public int? DaysBack { get; set; }
    /// <summary>Opaque cursor from a previous page's PageInfo.EndCursor.</summary>
    public string? After { get; set; }
    public int Take { get; set; } = 50;
}
