using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Nadosh.Infrastructure.Data;
using Nadosh.Core.Models;
using Nadosh.Api.Infrastructure;

namespace Nadosh.Api.Controllers;

/// <summary>
/// Edge Command & Control: Manage edge sites, agents, and authorized tasks
/// </summary>
[ApiController]
[ApiKeyAuth]
[Route("api/edge")]
public sealed class EdgeCommandController : ControllerBase
{
    private readonly NadoshDbContext _db;
    private readonly ILogger<EdgeCommandController> _logger;

    public EdgeCommandController(NadoshDbContext db, ILogger<EdgeCommandController> logger)
    {
        _db = db;
        _logger = logger;
    }

    #region Sites Management

    /// <summary>
    /// Get all edge sites
    /// </summary>
    [HttpGet("sites")]
    public async Task<IActionResult> GetSites()
    {
        var sites = await _db.EdgeSites
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync();

        return Ok(new { Count = sites.Count, Sites = sites });
    }

    /// <summary>
    /// Get specific edge site
    /// </summary>
    [HttpGet("sites/{siteId}")]
    public async Task<IActionResult> GetSite(string siteId)
    {
        var site = await _db.EdgeSites
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SiteId == siteId);

        if (site == null)
            return NotFound(new { Message = $"Site '{siteId}' not found" });

        return Ok(site);
    }

    /// <summary>
    /// Create or update an edge site
    /// </summary>
    [HttpPut("sites/{siteId}")]
    public async Task<IActionResult> UpsertSite(string siteId, [FromBody] EdgeSiteRequest request)
    {
        var site = await _db.EdgeSites.FindAsync(siteId);
        var isNew = site == null;

        if (isNew)
        {
            site = new EdgeSite
            {
                SiteId = siteId,
                Name = request.Name ?? siteId,
                AllowedCidrs = request.AllowedCidrs ?? new List<string>(),
                AllowedCapabilities = request.AllowedCapabilities ?? new List<string>(),
                CreatedAt = DateTime.UtcNow
            };
            _db.EdgeSites.Add(site);
        }
        else
        {
            site.Name = request.Name ?? site.Name;
            site.AllowedCidrs = request.AllowedCidrs ?? site.AllowedCidrs;
            site.AllowedCapabilities = request.AllowedCapabilities ?? site.AllowedCapabilities;
        }

        await _db.SaveChangesAsync();

        return isNew ? CreatedAtAction(nameof(GetSite), new { siteId }, site) : Ok(site);
    }

    /// <summary>
    /// Delete an edge site (fails if agents are still enrolled)
    /// </summary>
    [HttpDelete("sites/{siteId}")]
    public async Task<IActionResult> DeleteSite(string siteId)
    {
        var agentCount = await _db.EdgeAgents.CountAsync(a => a.SiteId == siteId);
        if (agentCount > 0)
            return Conflict(new { Message = $"Cannot delete site with {agentCount} enrolled agents" });

        var site = await _db.EdgeSites.FindAsync(siteId);
        if (site == null)
            return NotFound();

        _db.EdgeSites.Remove(site);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    #endregion

    #region Agents Management

    /// <summary>
    /// Enroll a new edge agent (called by agent on startup)
    /// </summary>
    [HttpPost("agents/enroll")]
    public async Task<IActionResult> EnrollAgent([FromBody] AgentEnrollmentRequest request)
    {
        // Ensure site exists
        var site = await _db.EdgeSites.FindAsync(request.SiteId);
        if (site == null)
        {
            // Auto-create site if it doesn't exist
            site = new EdgeSite
            {
                SiteId = request.SiteId,
                Name = request.SiteId,
                AllowedCidrs = new List<string> { "0.0.0.0/0" },
                AllowedCapabilities = new List<string> { "discovery", "scanning", "monitoring" },
                CreatedAt = DateTime.UtcNow
            };
            _db.EdgeSites.Add(site);
        }

        // Check if agent already exists
        var agent = await _db.EdgeAgents.FindAsync(request.AgentId);
        var isNew = agent == null;

        if (isNew)
        {
            agent = new EdgeAgent
            {
                AgentId = request.AgentId,
                SiteId = request.SiteId,
                Hostname = request.Hostname,
                OperatingSystem = request.Platform,
                Architecture = "unknown",
                AgentVersion = request.Version,
                AdvertisedCapabilities = request.Capabilities?.Keys.ToList() ?? new List<string>(),
                Status = EdgeAgentStatus.Active,
                EnrolledAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow
            };
            _db.EdgeAgents.Add(agent);
            _logger.LogInformation("New agent enrolled: {AgentId} at site {SiteId}", request.AgentId, request.SiteId);
        }
        else
        {
            // Re-enrollment (agent restarted)
            agent.Status = EdgeAgentStatus.Active;
            agent.LastSeenAt = DateTime.UtcNow;
            agent.AgentVersion = request.Version;
            agent.AdvertisedCapabilities = request.Capabilities?.Keys.ToList() ?? agent.AdvertisedCapabilities;
            _logger.LogInformation("Agent re-enrolled: {AgentId}", request.AgentId);
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            Status = isNew ? "enrolled" : "re-enrolled",
            AgentId = agent.AgentId,
            SiteId = agent.SiteId,
            Message = $"Welcome {agent.Hostname}! You are now connected to the mothership."
        });
    }

    /// <summary>
    /// Get all edge agents with optional filtering
    /// </summary>
    [HttpGet("agents")]
    public async Task<IActionResult> GetAgents(
        [FromQuery] string? siteId,
        [FromQuery] EdgeAgentStatus? status,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0)
    {
        var query = _db.EdgeAgents.AsQueryable();

        if (!string.IsNullOrEmpty(siteId))
            query = query.Where(a => a.SiteId == siteId);

        if (status.HasValue)
            query = query.Where(a => a.Status == status.Value);

        var total = await query.CountAsync();
        var agents = await query
            .OrderByDescending(a => a.LastSeenAt)
            .Skip(offset)
            .Take(Math.Min(limit, 1000))
            .Select(a => new
            {
                a.AgentId,
                a.SiteId,
                a.Status,
                a.Hostname,
                a.OperatingSystem,
                a.Architecture,
                a.AdvertisedCapabilities,
                a.EnrolledAt,
                a.LastSeenAt,
                MinutesSinceLastSeen = (DateTime.UtcNow - a.LastSeenAt).Value.TotalMinutes
            })
            .ToListAsync();

        return Ok(new { Total = total, Limit = limit, Offset = offset, Agents = agents });
    }

    /// <summary>
    /// Get specific agent details
    /// </summary>
    [HttpGet("agents/{agentId}")]
    public async Task<IActionResult> GetAgent(string agentId)
    {
        var agent = await _db.EdgeAgents
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AgentId == agentId);

        if (agent == null)
            return NotFound(new { Message = $"Agent '{agentId}' not found" });

        // Get task stats for this agent
        var taskStats = await _db.AuthorizedTasks
            .Where(t => t.AgentId == agentId || t.ClaimedByAgentId == agentId)
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        return Ok(new
        {
            Agent = agent,
            TaskStats = taskStats
        });
    }

    /// <summary>
    /// Receive heartbeat from agent (updates last seen timestamp)
    /// </summary>
    [HttpPost("agents/{agentId}/heartbeat")]
    public async Task<IActionResult> ReceiveHeartbeat(string agentId, [FromBody] AgentHeartbeatRequest heartbeat)
    {
        var agent = await _db.EdgeAgents.FindAsync(agentId);
        if (agent == null)
        {
            return NotFound(new { Message = $"Agent '{agentId}' not enrolled. Please enroll first." });
        }

        // Update agent status
        agent.LastSeenAt = DateTime.UtcNow;
        agent.Status = heartbeat.Status.ToLowerInvariant() == "active" 
            ? EdgeAgentStatus.Active 
            : EdgeAgentStatus.Disabled;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            Message = "Heartbeat received",
            AgentId = agentId,
            ServerTime = DateTime.UtcNow,
            Status = agent.Status.ToString()
        });
    }

    /// <summary>
    /// Manually set agent status (for maintenance/decommissioning)
    /// </summary>
    [HttpPatch("agents/{agentId}/status")]
    public async Task<IActionResult> UpdateAgentStatus(string agentId, [FromBody] AgentStatusUpdate request)
    {
        var agent = await _db.EdgeAgents.FindAsync(agentId);
        if (agent == null)
            return NotFound(new { Message = $"Agent '{agentId}' not found" });

        agent.Status = request.Status;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Agent {AgentId} status manually set to {Status}", agentId, request.Status);

        return Ok(new { AgentId = agentId, Status = agent.Status });
    }

    /// <summary>
    /// Force delete an agent and release its tasks
    /// </summary>
    [HttpDelete("agents/{agentId}")]
    public async Task<IActionResult> DeleteAgent(string agentId, [FromQuery] bool releaseTasks = true)
    {
        var agent = await _db.EdgeAgents.FindAsync(agentId);
        if (agent == null)
            return NotFound();

        if (releaseTasks)
        {
            // Release claimed tasks back to pending
            var claimedTasks = await _db.AuthorizedTasks
                .Where(t => t.ClaimedByAgentId == agentId && t.Status == AuthorizedTaskStatus.Claimed)
                .ToListAsync();

            foreach (var task in claimedTasks)
            {
                task.ClaimedByAgentId = null;
                task.LeaseExpiresAt = null;
                task.Status = AuthorizedTaskStatus.Queued;
            }

            _logger.LogInformation("Released {Count} tasks from deleted agent {AgentId}", claimedTasks.Count, agentId);
        }

        _db.EdgeAgents.Remove(agent);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    #endregion

    #region Task Management

    /// <summary>
    /// Get all authorized tasks with filtering
    /// </summary>
    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasks(
        [FromQuery] string? siteId,
        [FromQuery] string? agentId,
        [FromQuery] AuthorizedTaskStatus? status,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0)
    {
        var query = _db.AuthorizedTasks.AsQueryable();

        if (!string.IsNullOrEmpty(siteId))
            query = query.Where(t => t.SiteId == siteId);

        if (!string.IsNullOrEmpty(agentId))
            query = query.Where(t => t.AgentId == agentId || t.ClaimedByAgentId == agentId);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        var total = await query.CountAsync();
        var tasks = await query
            .OrderByDescending(t => t.IssuedAt)
            .Skip(offset)
            .Take(Math.Min(limit, 1000))
            .Select(t => new
            {
                t.TaskId,
                t.SiteId,
                t.AgentId,
                t.ClaimedByAgentId,
                t.TaskKind,
                t.Status,
                CreatedAt = t.IssuedAt,
                t.ExpiresAt,
                t.LeaseExpiresAt,
                ScopeJson = t.ScopeJson
            })
            .ToListAsync();

        return Ok(new { Total = total, Limit = limit, Offset = offset, Tasks = tasks });
    }

    /// <summary>
    /// Get specific task details
    /// </summary>
    [HttpGet("tasks/{taskId}")]
    public async Task<IActionResult> GetTask(string taskId)
    {
        var task = await _db.AuthorizedTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TaskId == taskId);

        if (task == null)
            return NotFound(new { Message = $"Task '{taskId}' not found" });

        return Ok(task);
    }

    /// <summary>
    /// Create a new authorized task for a site
    /// </summary>
    [HttpPost("tasks")]
    public async Task<IActionResult> CreateTask([FromBody] CreateTaskRequest request)
    {
        // Validate site exists
        var site = await _db.EdgeSites.FindAsync(request.SiteId);
        if (site == null)
            return NotFound(new { Message = $"Site '{request.SiteId}' not found" });

        var task = new AuthorizedTask
        {
            TaskId = Guid.NewGuid().ToString(),
            SiteId = request.SiteId,
            AgentId = request.AgentId, // Optional: specific agent or null for any
            TaskKind = request.TaskKind,
            ScopeJson = System.Text.Json.JsonSerializer.Serialize(request.Scope),
            RequiredCapabilities = request.RequiredCapabilities ?? new List<string>(),
            PayloadJson = request.Payload ?? "{}",
            Status = AuthorizedTaskStatus.Queued,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = request.ExpiresInMinutes.HasValue 
                ? DateTime.UtcNow.AddMinutes(request.ExpiresInMinutes.Value)
                : DateTime.UtcNow.AddHours(24)
        };

        _db.AuthorizedTasks.Add(task);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created task {TaskId} for site {SiteId}, kind {TaskKind}", 
            task.TaskId, task.SiteId, task.TaskKind);

        return CreatedAtAction(nameof(GetTask), new { taskId = task.TaskId }, task);
    }

    /// <summary>
    /// Cancel a pending or claimed task
    /// </summary>
    [HttpPost("tasks/{taskId}/cancel")]
    public async Task<IActionResult> CancelTask(string taskId)
    {
        var task = await _db.AuthorizedTasks.FindAsync(taskId);
        if (task == null)
            return NotFound();

        if (task.Status == AuthorizedTaskStatus.Completed || task.Status == AuthorizedTaskStatus.Failed)
            return Conflict(new { Message = "Cannot cancel task that is already completed or failed" });

        task.Status = AuthorizedTaskStatus.Failed;
        task.ClaimedByAgentId = null;
        task.LeaseExpiresAt = null;

        await _db.SaveChangesAsync();

        return Ok(new { TaskId = taskId, Status = task.Status });
    }

    /// <summary>
    /// Get execution records for tasks (agent-side buffered state)
    /// </summary>
    [HttpGet("execution-records")]
    public async Task<IActionResult> GetExecutionRecords(
        [FromQuery] string? agentId,
        [FromQuery] int limit = 100)
    {
        var query = _db.EdgeTaskExecutionRecords.AsQueryable();

        if (!string.IsNullOrEmpty(agentId))
            query = query.Where(r => r.AgentId == agentId);

        var records = await query
            .OrderByDescending(r => r.Id)
            .Take(Math.Min(limit, 1000))
            .ToListAsync();

        return Ok(new { Count = records.Count, Records = records });
    }

    #endregion

    #region Dashboard Stats

    /// <summary>
    /// Get edge fleet overview statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var stats = new
        {
            Sites = new
            {
                Total = await _db.EdgeSites.CountAsync()
            },
            Agents = new
            {
                Total = await _db.EdgeAgents.CountAsync(),
                Active = await _db.EdgeAgents.CountAsync(a => a.Status == EdgeAgentStatus.Active),
                Pending = await _db.EdgeAgents.CountAsync(a => a.Status == EdgeAgentStatus.Pending),
                Disabled = await _db.EdgeAgents.CountAsync(a => a.Status == EdgeAgentStatus.Disabled),
                RecentlyActive = await _db.EdgeAgents.CountAsync(a => 
                    a.LastSeenAt >= DateTime.UtcNow.AddMinutes(-5))
            },
            Tasks = new
            {
                Total = await _db.AuthorizedTasks.CountAsync(),
                Pending = await _db.AuthorizedTasks.CountAsync(t => t.Status == AuthorizedTaskStatus.Queued),
                Claimed = await _db.AuthorizedTasks.CountAsync(t => t.Status == AuthorizedTaskStatus.Claimed),
                Completed = await _db.AuthorizedTasks.CountAsync(t => t.Status == AuthorizedTaskStatus.Completed),
                Failed = await _db.AuthorizedTasks.CountAsync(t => t.Status == AuthorizedTaskStatus.Failed),
                Expired = await _db.AuthorizedTasks.CountAsync(t => 
                    t.Status == AuthorizedTaskStatus.Queued && t.ExpiresAt < DateTime.UtcNow)
            }
        };

        return Ok(stats);
    }

    /// <summary>
    /// Get agent activity timeline (heartbeat history approximation)
    /// </summary>
    [HttpGet("agents/{agentId}/activity")]
    public async Task<IActionResult> GetAgentActivity(string agentId, [FromQuery] int hours = 24)
    {
        var since = DateTime.UtcNow.AddHours(-hours);

        // Get audit events for heartbeats
        var heartbeats = await _db.AuditEvents
            .Where(e => e.Action == "edge-heartbeat" && 
                        e.Actor == agentId &&
                        e.Timestamp >= since)
            .OrderBy(e => e.Timestamp)
            .Select(e => new { e.Timestamp, e.Actor })
            .ToListAsync();

        return Ok(new
        {
            AgentId = agentId,
            SinceHours = hours,
            HeartbeatCount = heartbeats.Count,
            Heartbeats = heartbeats
        });
    }

    #endregion

    #region Installation Scripts

    /// <summary>
    /// Get PowerShell installation script (pipe to iex)
    /// Usage: iwr https://mothership/edge/install.ps1 | iex
    /// </summary>
    [HttpGet("install.ps1")]
    [AllowAnonymous]
    public IActionResult GetPowerShellInstallScript()
    {
        var mothershipUrl = $"{Request.Scheme}://{Request.Host}";
        
        // Return the static script from wwwroot
        var scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "scripts", "install-agent.ps1");
        if (System.IO.File.Exists(scriptPath))
        {
            var script = System.IO.File.ReadAllText(scriptPath);
            // Replace placeholder with actual mothership URL
            script = script.Replace("{{MOTHERSHIP_URL}}", mothershipUrl);
            return Content(script, "text/plain");
        }
        
        return NotFound("Installation script not found");
    }

    /// <summary>
    /// Get Bash installation script (pipe to bash)
    /// Usage: curl -sSL https://mothership/edge/install.sh | bash
    /// </summary>
    [HttpGet("install.sh")]
    [AllowAnonymous]
    public IActionResult GetBashInstallScript()
    {
        var mothershipUrl = $"{Request.Scheme}://{Request.Host}";
        
        var scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "scripts", "install-agent.sh");
        if (System.IO.File.Exists(scriptPath))
        {
            var script = System.IO.File.ReadAllText(scriptPath);
            script = script.Replace("{{MOTHERSHIP_URL}}", mothershipUrl);
            return Content(script, "text/x-shellscript");
        }
        
        return NotFound("Installation script not found");
    }

    #endregion
}

#region Request/Response Models

public sealed record EdgeSiteRequest
{
    public string? Name { get; init; }
    public List<string>? AllowedCidrs { get; init; }
    public List<string>? AllowedCapabilities { get; init; }
}

public sealed record AgentStatusUpdate
{
    public required EdgeAgentStatus Status { get; init; }
}

public sealed record AgentEnrollmentRequest
{
    public required string SiteId { get; init; }
    public required string AgentId { get; init; }
    public required string Hostname { get; init; }
    public required string Platform { get; init; }
    public required string Version { get; init; }
    public List<string>? WorkerRoles { get; init; }
    public Dictionary<string, object>? Capabilities { get; init; }
}

public sealed record AgentHeartbeatRequest
{
    public required string AgentId { get; init; }
    public required string Status { get; init; }
    public double? CpuUsage { get; init; }
    public double? MemoryUsage { get; init; }
    public string? Uptime { get; init; }
    public DateTimeOffset? LastSeen { get; init; }
}

public sealed record CreateTaskRequest
{
    public required string SiteId { get; init; }
    public string? AgentId { get; init; }
    public required string TaskKind { get; init; }
    public required AuthorizedTaskScope Scope { get; init; }
    public List<string>? RequiredCapabilities { get; init; }
    public string? Payload { get; init; }
    public int? ExpiresInMinutes { get; init; }
}

#endregion
