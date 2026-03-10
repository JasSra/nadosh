using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nadosh.Infrastructure.Data;
using Nadosh.Api.Infrastructure;

namespace Nadosh.Api.Controllers;

/// <summary>
/// Query services discovered during scanning
/// </summary>
[ApiController]
[ApiKeyAuth]
[Route("api/[controller]")]
public class ServicesController : ControllerBase
{
    private readonly NadoshDbContext _db;
    private readonly ILogger<ServicesController> _logger;

    public ServicesController(NadoshDbContext db, ILogger<ServicesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Get all discovered services grouped by service name
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? cidr = null)
    {
        var query = _db.Observations
            .Where(o => o.State == "open" && o.ServiceName != null);

        if (!string.IsNullOrEmpty(cidr))
        {
            query = query.Where(o => o.TargetId.StartsWith(cidr));
        }

        var services = await query
            .GroupBy(o => o.ServiceName)
            .Select(g => new
            {
                service = g.Key,
                hostCount = g.Select(x => x.TargetId).Distinct().Count(),
                totalPorts = g.Count(),
                commonPorts = g.Select(x => x.Port).Distinct().OrderBy(p => p).Take(10).ToList()
            })
            .OrderByDescending(s => s.hostCount)
            .ToListAsync();

        return Ok(new
        {
            total = services.Count,
            services
        });
    }

    /// <summary>
    /// Get all hosts running a specific service (e.g., "ssh", "https")
    /// </summary>
    [HttpGet("{serviceName}")]
    public async Task<IActionResult> GetByService(string serviceName)
    {
        var hosts = await _db.Observations
            .Where(o => o.State == "open" && o.ServiceName == serviceName.ToLower())
            .OrderByDescending(o => o.ObservedAt)
            .GroupBy(o => new { o.TargetId, o.Port })
            .Select(g => g.OrderByDescending(x => x.ObservedAt).First())
            .Select(o => new
            {
                ip = o.TargetId,
                o.Port,
                o.Banner,
                o.HttpTitle,
                o.HttpServer,
                o.SslSubject,
                lastSeen = o.ObservedAt,
                tier = o.Tier
            })
            .OrderBy(h => h.ip)
            .ToListAsync();

        return Ok(new
        {
            service = serviceName.ToLower(),
            hostCount = hosts.Select(h => h.ip).Distinct().Count(),
            totalInstances = hosts.Count,
            hosts
        });
    }

    /// <summary>
    /// Get all SSH hosts with banners
    /// </summary>
    [HttpGet("ssh/banners")]
    public async Task<IActionResult> GetSshBanners()
    {
        var sshHosts = await _db.Observations
            .Where(o => o.State == "open" && o.ServiceName == "ssh" && o.Banner != null)
            .OrderByDescending(o => o.ObservedAt)
            .GroupBy(o => o.TargetId)
            .Select(g => g.OrderByDescending(x => x.ObservedAt).First())
            .Select(o => new
            {
                ip = o.TargetId,
                o.Port,
                o.Banner,
                lastSeen = o.ObservedAt
            })
            .OrderBy(h => h.ip)
            .ToListAsync();

        // Group by banner version
        var versions = sshHosts
            .GroupBy(h => h.Banner)
            .Select(g => new
            {
                banner = g.Key,
                count = g.Count(),
                hosts = g.Select(h => h.ip).ToList()
            })
            .OrderByDescending(v => v.count)
            .ToList();

        return Ok(new
        {
            totalHosts = sshHosts.Count,
            hosts = sshHosts,
            versionDistribution = versions
        });
    }

    /// <summary>
    /// Get all HTTP/HTTPS services with titles
    /// </summary>
    [HttpGet("web/titles")]
    public async Task<IActionResult> GetWebTitles()
    {
        var webHosts = await _db.Observations
            .Where(o => o.State == "open" 
                && (o.ServiceName == "http" || o.ServiceName == "https")
                && o.HttpTitle != null)
            .OrderByDescending(o => o.ObservedAt)
            .GroupBy(o => new { o.TargetId, o.Port })
            .Select(g => g.OrderByDescending(x => x.ObservedAt).First())
            .Select(o => new
            {
                ip = o.TargetId,
                o.Port,
                o.ServiceName,
                o.HttpTitle,
                o.HttpServer,
                o.HttpStatusCode,
                lastSeen = o.ObservedAt
            })
            .OrderBy(h => h.ip)
            .ToListAsync();

        return Ok(new
        {
            totalWebServices = webHosts.Count,
            hosts = webHosts
        });
    }

    /// <summary>
    /// Get all TLS/SSL certificates
    /// </summary>
    [HttpGet("tls/certificates")]
    public async Task<IActionResult> GetTlsCertificates()
    {
        var tlsHosts = await _db.Observations
            .Where(o => o.State == "open" && o.SslSubject != null)
            .OrderByDescending(o => o.ObservedAt)
            .GroupBy(o => new { o.TargetId, o.Port })
            .Select(g => g.OrderByDescending(x => x.ObservedAt).First())
            .Select(o => new
            {
                ip = o.TargetId,
                o.Port,
                o.SslSubject,
                o.SslIssuer,
                o.SslExpiry,
                isSelfSigned = o.SslSubject == o.SslIssuer,
                lastSeen = o.ObservedAt
            })
            .OrderBy(h => h.ip)
            .ToListAsync();

        return Ok(new
        {
            totalTlsServices = tlsHosts.Count,
            selfSignedCount = tlsHosts.Count(h => h.isSelfSigned),
            hosts = tlsHosts
        });
    }
}
