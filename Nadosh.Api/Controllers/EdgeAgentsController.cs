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

    [HttpPost("{agentId}/tasks/{taskId}/claim")]
    public async Task<IActionResult> ClaimTask(string agentId, string taskId, [FromBody] EdgeTaskClaimRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _edgeControlPlaneService.ClaimTaskAsync(agentId, taskId, request, cancellationToken);
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

    [HttpPost("{agentId}/tasks/{taskId}/complete")]
    public async Task<IActionResult> CompleteTask(string agentId, string taskId, [FromBody] EdgeTaskCompletionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _edgeControlPlaneService.CompleteTaskAsync(agentId, taskId, request, cancellationToken);
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

    [HttpPost("{agentId}/tasks/{taskId}/fail")]
    public async Task<IActionResult> FailTask(string agentId, string taskId, [FromBody] EdgeTaskFailureRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _edgeControlPlaneService.FailTaskAsync(agentId, taskId, request, cancellationToken);
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
}
