using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nadosh.Infrastructure.Data;
using Nadosh.Core.Models;
using Nadosh.Api.Infrastructure;

namespace Nadosh.Api.Controllers;

[ApiController]
[ApiKeyAuth]
[Route("v1/[controller]")]
public class CertificatesController : ControllerBase
{
    private readonly NadoshDbContext _dbContext;

    public CertificatesController(NadoshDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("{fingerprint}")]
    public async Task<IActionResult> GetCertificateByFingerprint(string fingerprint, CancellationToken ct)
    {
        var certs = await _dbContext.CertificateObservations
            .AsNoTracking()
            .Where(c => c.Sha256 == fingerprint)
            .ToListAsync(ct);

        if (!certs.Any()) return NotFound();

        return Ok(certs);
    }

    [HttpGet("by-domain/{domain}")]
    public async Task<IActionResult> GetCertificatesByDomain(string domain, CancellationToken ct)
    {
        // Simple LIKE query for MVP. For scale, use Postgres full-text search or specialized indexing over SAN lists.
        var certs = await _dbContext.CertificateObservations
            .AsNoTracking()
            .Where(c => c.Subject.Contains(domain) || (c.SanList != null && c.SanList.Contains(domain)))
            .ToListAsync(ct);

        return Ok(certs);
    }

    [HttpPost("search")]
    public async Task<IActionResult> SearchCertificates([FromBody] CertificateSearchRequest request, CancellationToken ct)
    {
        var take = Math.Clamp(request.Take, 1, 200);
        var query = _dbContext.CertificateObservations.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(request.Subject))
            query = query.Where(c => c.Subject.Contains(request.Subject));

        if (!string.IsNullOrEmpty(request.Issuer))
            query = query.Where(c => c.Issuer.Contains(request.Issuer));

        if (!string.IsNullOrEmpty(request.San))
            query = query.Where(c => c.SanList.Any(s => s.Contains(request.San)));

        if (!string.IsNullOrEmpty(request.Ip))
            query = query.Where(c => c.TargetId == request.Ip);

        if (request.ExpiryBefore.HasValue)
            query = query.Where(c => c.ValidTo <= request.ExpiryBefore.Value);

        if (request.ExpiryAfter.HasValue)
            query = query.Where(c => c.ValidTo >= request.ExpiryAfter.Value);

        if (request.ExpiredOnly == true)
            query = query.Where(c => c.IsExpired);

        if (request.SelfSignedOnly == true)
            query = query.Where(c => c.IsSelfSigned);

        // Cursor-based pagination
        if (!string.IsNullOrEmpty(request.After) && long.TryParse(Base64UrlDecode(request.After), out var cursorId))
            query = query.Where(c => c.Id < cursorId);

        var results = await query
            .OrderByDescending(c => c.ObservedAt)
            .ThenByDescending(c => c.Id)
            .Take(take + 1)
            .ToListAsync(ct);

        var hasNextPage = results.Count > take;
        if (hasNextPage) results = results.Take(take).ToList();
        var endCursor = results.Count > 0 ? Base64UrlEncode(results[^1].Id.ToString()) : null;

        return Ok(new { pageInfo = new { hasNextPage, endCursor }, count = results.Count, results });
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

public class CertificateSearchRequest
{
    public string? Subject { get; set; }
    public string? Issuer { get; set; }
    public string? San { get; set; }
    public string? Ip { get; set; }
    public DateTime? ExpiryBefore { get; set; }
    public DateTime? ExpiryAfter { get; set; }
    public bool? ExpiredOnly { get; set; }
    public bool? SelfSignedOnly { get; set; }
    /// <summary>Opaque cursor from a previous page's PageInfo.EndCursor.</summary>
    public string? After { get; set; }
    public int Take { get; set; } = 50;
}
