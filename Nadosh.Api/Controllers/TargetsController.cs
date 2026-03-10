using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nadosh.Infrastructure.Data;
using Nadosh.Core.Models;
using Nadosh.Api.Infrastructure;

namespace Nadosh.Api.Controllers;

[ApiController]
[ApiKeyAuth]
[Route("v1/[controller]")]
public class TargetsController : ControllerBase
{
    private readonly NadoshDbContext _dbContext;

    public TargetsController(NadoshDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetTargets([FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var total = await _dbContext.Targets.CountAsync(ct);
        var targets = await _dbContext.Targets
            .AsNoTracking()
            .OrderBy(t => t.Ip)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return Ok(new { TotalCount = total, Results = targets });
    }

    [HttpGet("{ip}")]
    public async Task<IActionResult> GetTarget(string ip, CancellationToken ct)
    {
        var target = await _dbContext.Targets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Ip == ip, ct);

        if (target == null) return NotFound();
        return Ok(target);
    }

    [HttpPost("demo-scan")]
    public async Task<IActionResult> TriggerLocalNetworkScan(CancellationToken ct)
    {
        // Add 192.168.4.1 to 192.168.4.254 to the Targets table
        var baseIp = "192.168.4.";
        var targets = new List<Target>();

        for (int i = 1; i <= 254; i++)
        {
            var ip = $"{baseIp}{i}";
            var exists = await _dbContext.Targets.AnyAsync(t => t.Ip == ip, ct);
            if (!exists)
            {
                targets.Add(new Target
                {
                    Ip = ip,
                    CidrSource = "192.168.4.0/24",
                    Monitored = true,
                    NextScheduled = DateTime.UtcNow,
                    OwnershipTags = new List<string> { "demo", "local-network" }
                });
            }
            else
            {
                // Force an immediate rescan
                var existing = await _dbContext.Targets.FirstAsync(t => t.Ip == ip, ct);
                existing.NextScheduled = DateTime.UtcNow;
            }
        }

        if (targets.Any())
        {
            _dbContext.Targets.AddRange(targets);
        }

        await _dbContext.SaveChangesAsync(ct);

        return Ok(new { Message = $"Scheduled {targets.Count} local network IPs (192.168.4.1 - 254) for immediate scanning." });
    }
}
