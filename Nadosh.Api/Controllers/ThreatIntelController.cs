using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nadosh.Api.Infrastructure;
using Nadosh.Core.Services;
using Nadosh.Infrastructure.Data;

namespace Nadosh.Api.Controllers;

[ApiController]
[ApiKeyAuth]
[Route("v1/[controller]")]
public class ThreatIntelController : ControllerBase
{
    private readonly NadoshDbContext _dbContext;
    private readonly ThreatScoringService _threatService;
    private readonly MitreAttackMappingService _mitreService;
    private readonly ILogger<ThreatIntelController> _logger;

    public ThreatIntelController(
        NadoshDbContext dbContext,
        ThreatScoringService threatService,
        MitreAttackMappingService mitreService,
        ILogger<ThreatIntelController> logger)
    {
        _dbContext = dbContext;
        _threatService = threatService;
        _mitreService = mitreService;
        _logger = logger;
    }

    /// <summary>
    /// Get top threats by risk score
    /// </summary>
    [HttpGet("top-threats")]
    public async Task<IActionResult> GetTopThreats(
        [FromQuery] int take = 50,
        [FromQuery] double? minScore = null,
        CancellationToken ct = default)
    {
        var query = _dbContext.CurrentExposures
            .AsNoTracking()
            .Where(e => e.ThreatScore != null && e.CurrentState == "open");

        if (minScore.HasValue)
            query = query.Where(e => e.ThreatScore >= minScore.Value);

        var threats = await query
            .OrderByDescending(e => e.ThreatScore)
            .ThenByDescending(e => e.HighestCvssScore)
            .Take(take)
            .Select(e => new
            {
                e.TargetId,
                e.Port,
                e.Protocol,
                e.Classification,
                e.ThreatScore,
                e.ThreatLevel,
                e.ThreatExplanation,
                e.HighestCvssScore,
                e.CveSeverity,
                e.CveIds,
                e.MitreTactics,
                e.MitreTechniques,
                e.FirstSeen,
                e.LastSeen,
                e.LastChanged
            })
            .ToListAsync(ct);

        return Ok(new
        {
            TotalCount = threats.Count,
            Take = take,
            Threats = threats
        });
    }

    /// <summary>
    /// Get threat statistics and distribution
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetThreatStats(CancellationToken ct)
    {
        var exposuresWithScores = await _dbContext.CurrentExposures
            .Where(e => e.ThreatScore != null)
            .CountAsync(ct);

        var threatLevelCounts = await _dbContext.CurrentExposures
            .Where(e => e.ThreatLevel != null)
            .GroupBy(e => e.ThreatLevel)
            .Select(g => new { Level = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var avgScore = await _dbContext.CurrentExposures
            .Where(e => e.ThreatScore != null)
            .AverageAsync(e => e.ThreatScore, ct);

        var topMitreTactics = await _dbContext.CurrentExposures
            .Where(e => e.MitreTactics != null && e.MitreTactics != string.Empty)
            .ToListAsync(ct);

        var tacticCounts = topMitreTactics
            .SelectMany(e => e.MitreTactics!.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .GroupBy(t => t.Trim())
            .Select(g => new { Tactic = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToList();

        var topThreats = await _dbContext.CurrentExposures
            .Where(e => e.ThreatScore != null)
            .OrderByDescending(e => e.ThreatScore)
            .Take(10)
            .Select(e => new
            {
                e.TargetId,
                e.Port,
                e.Classification,
                e.ThreatScore,
                e.ThreatLevel
            })
            .ToListAsync(ct);

        return Ok(new
        {
            ExposuresScored = exposuresWithScores,
            AverageThreatScore = Math.Round(avgScore ?? 0, 1),
            ThreatLevels = threatLevelCounts,
            TopMitreTactics = tacticCounts,
            Top10Threats = topThreats,
            LastUpdated = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Get MITRE ATT&CK coverage for all exposures
    /// </summary>
    [HttpGet("mitre/coverage")]
    public async Task<IActionResult> GetMitreCoverage(CancellationToken ct)
    {
        var exposuresWithMitre = await _dbContext.CurrentExposures
            .Where(e => e.MitreTactics != null && e.MitreTactics != string.Empty)
            .ToListAsync(ct);

        var allTactics = exposuresWithMitre
            .SelectMany(e => e.MitreTactics!.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(t => t.Trim())
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var allTechniques = exposuresWithMitre
            .Where(e => e.MitreTechniques != null)
            .SelectMany(e => e.MitreTechniques!.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(t => t.Trim())
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var tacticBreakdown = exposuresWithMitre
            .SelectMany(e => e.MitreTactics!.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()))
            .GroupBy(t => t)
            .Select(g => new
            {
                Tactic = g.Key,
                Count = g.Count(),
                Percentage = Math.Round((double)g.Count() / exposuresWithMitre.Count * 100, 1)
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        return Ok(new
        {
            TotalExposures = exposuresWithMitre.Count,
            UniqueTactics = allTactics.Count,
            UniqueTechniques = allTechniques.Count,
            Tactics = allTactics,
            Techniques = allTechniques,
            TacticBreakdown = tacticBreakdown
        });
    }

    /// <summary>
    /// Search exposures by MITRE tactic or technique
    /// </summary>
    [HttpGet("mitre/search")]
    public async Task<IActionResult> SearchByMitre(
        [FromQuery] string? tactic = null,
        [FromQuery] string? technique = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var query = _dbContext.CurrentExposures.AsNoTracking();

        if (!string.IsNullOrEmpty(tactic))
        {
            query = query.Where(e => e.MitreTactics != null && e.MitreTactics.Contains(tactic));
        }

        if (!string.IsNullOrEmpty(technique))
        {
            query = query.Where(e => e.MitreTechniques != null && e.MitreTechniques.Contains(technique));
        }

        var total = await query.CountAsync(ct);

        var results = await query
            .OrderByDescending(e => e.ThreatScore)
            .Skip(skip)
            .Take(take)
            .Select(e => new
            {
                e.TargetId,
                e.Port,
                e.Protocol,
                e.Classification,
                e.ThreatScore,
                e.ThreatLevel,
                e.MitreTactics,
                e.MitreTechniques,
                e.FirstSeen,
                e.LastSeen
            })
            .ToListAsync(ct);

        return Ok(new
        {
            TotalCount = total,
            Skip = skip,
            Take = take,
            Tactic = tactic,
            Technique = technique,
            Results = results
        });
    }

    /// <summary>
    /// Calculate threat score on-demand for a specific IP:Port
    /// </summary>
    [HttpPost("calculate/{ip}/{port}")]
    public async Task<IActionResult> CalculateThreatScore(string ip, int port, CancellationToken ct)
    {
        var exposure = await _dbContext.CurrentExposures
            .Where(e => e.TargetId == ip && e.Port == port)
            .FirstOrDefaultAsync(ct);

        if (exposure == null)
            return NotFound(new { Message = $"No exposure found for {ip}:{port}" });

        var target = await _dbContext.Targets
            .Where(t => t.Ip == ip)
            .FirstOrDefaultAsync(ct);

        var threatScore = _threatService.CalculateThreatScore(exposure, target);
        var mitreMapping = _mitreService.MapExposureToMitre(exposure);

        // Update database
        exposure.ThreatScore = threatScore.Score;
        exposure.ThreatLevel = threatScore.Severity;
        exposure.ThreatExplanation = threatScore.Explanation;
        exposure.ThreatScoreCalculatedAt = DateTime.UtcNow;
        exposure.MitreTactics = mitreMapping.GetTacticsString();
        exposure.MitreTechniques = mitreMapping.GetTechniquesString();

        await _dbContext.SaveChangesAsync(ct);

        return Ok(new
        {
            IpAddress = ip,
            Port = port,
            ThreatScore = threatScore.Score,
            ThreatLevel = threatScore.Severity,
            Explanation = threatScore.Explanation,
            Components = threatScore.Components,
            MitreTactics = mitreMapping.Tactics.OrderBy(t => t).ToList(),
            MitreTechniques = mitreMapping.Techniques.OrderBy(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            CalculatedAt = DateTime.UtcNow
        });
    }
}
