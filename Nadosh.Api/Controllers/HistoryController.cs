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
    public async Task<IActionResult> GetHistory(string ip, [FromQuery] int? port, [FromQuery] DateTime? since, CancellationToken ct)
    {
        var query = _dbContext.Observations
            .AsNoTracking()
            .Where(o => o.TargetId == ip);

        if (port.HasValue)
        {
            query = query.Where(o => o.Port == port.Value);
        }

        if (since.HasValue)
        {
            // Crucial for PG partition pruning
            query = query.Where(o => o.ObservedAt >= since.Value);
        }
        else
        {
            // Default 30-day window to enforce partition pruning if none provided 
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
            query = query.Where(o => o.ObservedAt >= thirtyDaysAgo);
        }

        var results = await query
            .OrderByDescending(o => o.ObservedAt)
            .Take(100)
            .ToListAsync(ct);

        return Ok(results);
    }
}
