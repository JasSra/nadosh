using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nadosh.Api.Infrastructure;
using Nadosh.Core.Services;
using Nadosh.Infrastructure.Data;

namespace Nadosh.Api.Controllers;

[ApiController]
[ApiKeyAuth]
[Route("v1/[controller]")]
public class CveController : ControllerBase
{
    private readonly NadoshDbContext _dbContext;
    private readonly CveEnrichmentService _cveService;
    private readonly ILogger<CveController> _logger;

    public CveController(
        NadoshDbContext dbContext, 
        CveEnrichmentService cveService,
        ILogger<CveController> logger)
    {
        _dbContext = dbContext;
        _cveService = cveService;
        _logger = logger;
    }

    /// <summary>
    /// Get CVE information for a specific IP:Port exposure
    /// </summary>
    [HttpGet("{ip}/{port}")]
    public async Task<IActionResult> GetCvesForExposure(string ip, int port, CancellationToken ct)
    {
        var exposure = await _dbContext.CurrentExposures
            .AsNoTracking()
            .Where(e => e.TargetId == ip && e.Port == port)
            .FirstOrDefaultAsync(ct);

        if (exposure == null)
            return NotFound(new { Message = $"No exposure found for {ip}:{port}" });

        if (string.IsNullOrEmpty(exposure.CveIds))
        {
            return Ok(new
            {
                Ip = ip,
                Port = port,
                CveCount = 0,
                CveLastChecked = exposure.CveLastChecked,
                Message = "No CVEs found for this exposure"
            });
        }

        var cveIds = exposure.CveIds.Split(',', StringSplitOptions.RemoveEmptyEntries);

        return Ok(new
        {
            Ip = ip,
            Port = port,
            CveCount = cveIds.Length,
            CveIds = cveIds,
            HighestCvssScore = exposure.HighestCvssScore,
            CveSeverity = exposure.CveSeverity,
            CveLastChecked = exposure.CveLastChecked
        });
    }

    /// <summary>
    /// Get detailed CVE information by CVE ID
    /// </summary>
    [HttpGet("details/{cveId}")]
    public async Task<IActionResult> GetCveDetails(string cveId, CancellationToken ct)
    {
        var cveResult = await _cveService.GetCveByIdAsync(cveId, ct);

        if (cveResult == null)
            return NotFound(new { Message = $"CVE {cveId} not found" });

        return Ok(cveResult);
    }

    /// <summary>
    /// Search for CVEs by product/vendor/version
    /// </summary>
    [HttpPost("search")]
    public async Task<IActionResult> SearchCves([FromBody] CveSearchRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Product))
            return BadRequest(new { Message = "Product name is required" });

        var results = await _cveService.SearchCvesAsync(
            request.Product, 
            request.Vendor, 
            request.Version, 
            ct);

        return Ok(new
        {
            Product = request.Product,
            Vendor = request.Vendor,
            Version = request.Version,
            CveCount = results.Count,
            Results = results
        });
    }

    /// <summary>
    /// Get all exposures with known CVEs, ordered by severity
    /// </summary>
    [HttpGet("exposures")]
    public async Task<IActionResult> GetExposuresWithCves(
        [FromQuery] string? severity = null,
        [FromQuery] double? minCvssScore = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var query = _dbContext.CurrentExposures
            .AsNoTracking()
            .Where(e => e.CveIds != null && e.CveIds != string.Empty);

        if (!string.IsNullOrEmpty(severity))
            query = query.Where(e => e.CveSeverity == severity);

        if (minCvssScore.HasValue)
            query = query.Where(e => e.HighestCvssScore >= minCvssScore.Value);

        var total = await query.CountAsync(ct);

        var exposures = await query
            .OrderByDescending(e => e.HighestCvssScore)
            .ThenByDescending(e => e.LastSeen)
            .Skip(skip)
            .Take(take)
            .Select(e => new
            {
                e.TargetId,
                e.Port,
                e.Protocol,
                e.Classification,
                e.Severity,
                e.CveIds,
                e.HighestCvssScore,
                e.CveSeverity,
                e.CveLastChecked,
                e.FirstSeen,
                e.LastSeen,
                CveCount = e.CveIds != null ? e.CveIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Length : 0
            })
            .ToListAsync(ct);

        return Ok(new
        {
            TotalCount = total,
            Skip = skip,
            Take = take,
            Exposures = exposures
        });
    }

    /// <summary>
    /// Get CVE statistics across all exposures
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetCveStats(CancellationToken ct)
    {
        var totalExposures = await _dbContext.CurrentExposures.CountAsync(ct);
        
        var exposuresWithCves = await _dbContext.CurrentExposures
            .Where(e => e.CveIds != null && e.CveIds != string.Empty)
            .CountAsync(ct);

        var severityCounts = await _dbContext.CurrentExposures
            .Where(e => e.CveSeverity != null)
            .GroupBy(e => e.CveSeverity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var highestCvss = await _dbContext.CurrentExposures
            .Where(e => e.HighestCvssScore != null)
            .OrderByDescending(e => e.HighestCvssScore)
            .Take(10)
            .Select(e => new
            {
                e.TargetId,
                e.Port,
                e.Classification,
                e.HighestCvssScore,
                e.CveSeverity,
                e.CveIds
            })
            .ToListAsync(ct);

        return Ok(new
        {
            TotalExposures = totalExposures,
            ExposuresWithCves = exposuresWithCves,
            CoveragePercentage = totalExposures > 0 ? Math.Round((double)exposuresWithCves / totalExposures * 100, 2) : 0,
            SeverityCounts = severityCounts,
            Top10ByCvss = highestCvss,
            LastUpdated = DateTime.UtcNow
        });
    }
}

public class CveSearchRequest
{
    public string Product { get; set; } = string.Empty;
    public string? Vendor { get; set; }
    public string? Version { get; set; }
}
