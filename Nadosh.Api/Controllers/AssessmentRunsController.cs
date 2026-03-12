using Microsoft.AspNetCore.Mvc;
using Nadosh.Api.Infrastructure;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Api.Controllers;

[ApiController]
[ApiKeyAuth]
[Route("v1/[controller]")]
public sealed class AssessmentRunsController : ControllerBase
{
    private readonly IAssessmentRunService _assessmentRunService;
    private readonly IAssessmentRunRepository _assessmentRunRepository;

    public AssessmentRunsController(
        IAssessmentRunService assessmentRunService,
        IAssessmentRunRepository assessmentRunRepository)
    {
        _assessmentRunService = assessmentRunService;
        _assessmentRunRepository = assessmentRunRepository;
    }

    /// <summary>
    /// Submits an assessment run request for policy evaluation and queuing.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(AssessmentRunResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(AssessmentRunResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Submit([FromBody] SubmitAssessmentRunRequest request, CancellationToken cancellationToken)
    {
        var result = await _assessmentRunService.SubmitAsync(new AssessmentRunSubmissionRequest
        {
            ToolId = request.ToolId,
            RequestedBy = request.RequestedBy,
            TargetScope = request.TargetScope,
            ScopeKind = request.ScopeKind,
            Environment = request.Environment,
            ParametersJson = request.ParametersJson,
            ApprovalReference = request.ApprovalReference,
            DryRun = request.DryRun,
            ScopeTags = request.ScopeTags
        }, cancellationToken);

        var response = Map(result.Run, result.PolicyEvaluation);

        if (result.Run.Status == AssessmentRunStatus.Denied)
        {
            return StatusCode(StatusCodes.Status403Forbidden, response);
        }

        return AcceptedAtAction(nameof(GetById), new { runId = result.Run.RunId }, response);
    }

    /// <summary>
    /// Retrieves a single assessment run by identifier.
    /// </summary>
    [HttpGet("{runId}")]
    [ProducesResponseType(typeof(AssessmentRunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string runId, CancellationToken cancellationToken)
    {
        var run = await _assessmentRunRepository.GetByIdAsync(runId, cancellationToken);
        if (run is null)
        {
            return NotFound(new { Message = $"Assessment run '{runId}' was not found." });
        }

        return Ok(Map(run));
    }

    /// <summary>
    /// Lists assessment runs by status for review workflows.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AssessmentRunListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] AssessmentRunStatus? status, [FromQuery] int take = 50, CancellationToken cancellationToken = default)
    {
        if (status is null)
        {
            return BadRequest(new
            {
                Error = "status is required.",
                AllowedValues = Enum.GetNames<AssessmentRunStatus>()
            });
        }

        var runs = await _assessmentRunRepository.GetByStatusAsync(status.Value, take, cancellationToken);
        return Ok(new AssessmentRunListResponse
        {
            Count = runs.Count,
            Results = runs.Select(run => Map(run)).ToArray()
        });
    }

    private static AssessmentRunResponse Map(AssessmentRun run, AssessmentPolicyEvaluation? policyEvaluation = null)
    {
        return new AssessmentRunResponse
        {
            RunId = run.RunId,
            ToolId = run.ToolId,
            RequestedBy = run.RequestedBy,
            TargetScope = run.TargetScope,
            ScopeKind = run.ScopeKind,
            Environment = run.Environment,
            ApprovalReference = run.ApprovalReference,
            DryRun = run.DryRun,
            RequiresApproval = run.RequiresApproval,
            Status = run.Status,
            ParametersJson = run.ParametersJson,
            PolicyDecisionJson = run.PolicyDecisionJson,
            ResultSummaryJson = run.ResultSummaryJson,
            CreatedAt = run.CreatedAt,
            UpdatedAt = run.UpdatedAt,
            SubmittedAt = run.SubmittedAt,
            StartedAt = run.StartedAt,
            CompletedAt = run.CompletedAt,
            PolicyEvaluation = policyEvaluation
        };
    }
}

public sealed class SubmitAssessmentRunRequest
{
    /// <summary>Registered assessment tool identifier.</summary>
    public string ToolId { get; set; } = string.Empty;

    /// <summary>Human or system identity requesting the run.</summary>
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>IP, CIDR, hostname, service, application, or workflow scope.</summary>
    public string TargetScope { get; set; } = string.Empty;

    /// <summary>Kind of target scope being assessed.</summary>
    public AssessmentScopeKind ScopeKind { get; set; } = AssessmentScopeKind.IpAddress;

    /// <summary>Execution environment for the run.</summary>
    public AssessmentExecutionEnvironment Environment { get; set; } = AssessmentExecutionEnvironment.Lab;

    /// <summary>Tool-specific parameters encoded as JSON.</summary>
    public string ParametersJson { get; set; } = "{}";

    /// <summary>Optional change-control or approval reference.</summary>
    public string? ApprovalReference { get; set; }

    /// <summary>True to request a dry-run/preflight only.</summary>
    public bool DryRun { get; set; }

    /// <summary>Scope and authorization tags used by policy evaluation.</summary>
    public string[] ScopeTags { get; set; } = [];
}

public sealed class AssessmentRunResponse
{
    public string RunId { get; init; } = string.Empty;
    public string ToolId { get; init; } = string.Empty;
    public string RequestedBy { get; init; } = string.Empty;
    public string TargetScope { get; init; } = string.Empty;
    public AssessmentScopeKind ScopeKind { get; init; }
    public AssessmentExecutionEnvironment Environment { get; init; }
    public string? ApprovalReference { get; init; }
    public bool DryRun { get; init; }
    public bool RequiresApproval { get; init; }
    public AssessmentRunStatus Status { get; init; }
    public string ParametersJson { get; init; } = "{}";
    public string PolicyDecisionJson { get; init; } = "{}";
    public string? ResultSummaryJson { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? SubmittedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public AssessmentPolicyEvaluation? PolicyEvaluation { get; init; }
}

public sealed class AssessmentRunListResponse
{
    public int Count { get; init; }
    public IReadOnlyCollection<AssessmentRunResponse> Results { get; init; } = Array.Empty<AssessmentRunResponse>();
}
