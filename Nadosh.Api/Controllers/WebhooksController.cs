using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nadosh.Infrastructure.Data;
using Nadosh.Api.Infrastructure;

namespace Nadosh.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[ApiKeyAuth]
public sealed class WebhooksController : ControllerBase
{
    private readonly NadoshDbContext _db;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(NadoshDbContext db, ILogger<WebhooksController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Get paginated webhook delivery history
    /// </summary>
    /// <param name="eventType">Filter by event type (optional)</param>
    /// <param name="success">Filter by success status (optional)</param>
    /// <param name="since">Filter deliveries since timestamp (optional)</param>
    /// <param name="limit">Max results (default 100, max 1000)</param>
    /// <param name="offset">Skip N results for pagination</param>
    [HttpGet("deliveries")]
    public async Task<IActionResult> GetDeliveries(
        [FromQuery] string? eventType,
        [FromQuery] bool? success,
        [FromQuery] DateTime? since,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0)
    {
        limit = Math.Min(limit, 1000);

        var query = _db.WebhookDeliveries.AsQueryable();

        if (!string.IsNullOrEmpty(eventType))
            query = query.Where(d => d.EventType == eventType);

        if (success.HasValue)
            query = query.Where(d => d.Success == success.Value);

        if (since.HasValue)
            query = query.Where(d => d.SentAt >= since.Value);

        var total = await query.CountAsync();
        var deliveries = await query
            .OrderByDescending(d => d.SentAt)
            .Skip(offset)
            .Take(limit)
            .Select(d => new
            {
                d.Id,
                d.EventType,
                d.Url,
                d.Success,
                d.HttpStatusCode,
                d.ErrorMessage,
                d.SentAt,
                PayloadPreview = d.Payload.Length > 200 
                    ? d.Payload.Substring(0, 200) + "..." 
                    : d.Payload
            })
            .ToListAsync();

        return Ok(new
        {
            total,
            limit,
            offset,
            deliveries
        });
    }

    /// <summary>
    /// Get full details of a specific webhook delivery
    /// </summary>
    [HttpGet("deliveries/{id:guid}")]
    public async Task<IActionResult> GetDelivery(Guid id)
    {
        var delivery = await _db.WebhookDeliveries
            .Where(d => d.Id == id)
            .Select(d => new
            {
                d.Id,
                d.EventType,
                d.Url,
                d.Payload,
                d.Success,
                d.HttpStatusCode,
                d.ErrorMessage,
                d.SentAt
            })
            .FirstOrDefaultAsync();

        if (delivery == null)
            return NotFound(new { Message = "Webhook delivery not found" });

        return Ok(delivery);
    }

    /// <summary>
    /// Get webhook delivery statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats([FromQuery] DateTime? since)
    {
        var query = _db.WebhookDeliveries.AsQueryable();

        if (since.HasValue)
            query = query.Where(d => d.SentAt >= since.Value);

        var stats = await query
            .GroupBy(d => 1)
            .Select(g => new
            {
                TotalDeliveries = g.Count(),
                SuccessCount = g.Count(d => d.Success),
                FailureCount = g.Count(d => !d.Success),
                SuccessRate = g.Count() > 0 
                    ? Math.Round(g.Count(d => d.Success) * 100.0 / g.Count(), 2) 
                    : 0
            })
            .FirstOrDefaultAsync();

        var byEventType = await query
            .GroupBy(d => d.EventType)
            .Select(g => new
            {
                EventType = g.Key,
                Count = g.Count(),
                SuccessCount = g.Count(d => d.Success),
                FailureCount = g.Count(d => !d.Success)
            })
            .ToListAsync();

        return Ok(new
        {
            overall = stats ?? new { TotalDeliveries = 0, SuccessCount = 0, FailureCount = 0, SuccessRate = 0.0 },
            byEventType
        });
    }
}
