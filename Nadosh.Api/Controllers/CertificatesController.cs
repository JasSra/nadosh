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
}
