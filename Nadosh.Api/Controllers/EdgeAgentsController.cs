using Microsoft.AspNetCore.Mvc;
using Nadosh.Api.Infrastructure;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Api.Controllers;

[ApiController]
[ApiKeyAuth]
[Route("v1/edge-agents")]
public sealed class EdgeAgentsController : ControllerBase
{
    private readonly IEdgeControlPlaneService _edgeControlPlaneService;

    public EdgeAgentsController(IEdgeControlPlaneService edgeControlPlaneService)
    {
        _edgeControlPlaneService = edgeControlPlaneService;
    }

    [HttpPost("enroll")]
    public async Task<IActionResult> Enroll([FromBody] EdgeEnrollmentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _edgeControlPlaneService.EnrollAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return ValidationProblem(detail: ex.Message);
        }
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] EdgeHeartbeatRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _edgeControlPlaneService.RecordHeartbeatAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return ValidationProblem(detail: ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { Message = ex.Message });
        }
    }

    [HttpGet("{agentId}/tasks")]
    public async Task<IActionResult> GetPendingTasks(string agentId, CancellationToken cancellationToken)
    {
        var tasks = await _edgeControlPlaneService.GetPendingTasksAsync(agentId, cancellationToken);
        return Ok(new { Count = tasks.Count, Results = tasks });
    }
}
