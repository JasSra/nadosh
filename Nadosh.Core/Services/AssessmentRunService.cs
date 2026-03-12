using System.Text.Json;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Core.Services;

public sealed class AssessmentRunService : IAssessmentRunService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IAssessmentRunRepository _repository;
    private readonly IAssessmentPolicyService _policyService;
    private readonly IAuditService _auditService;

    public AssessmentRunService(
        IAssessmentRunRepository repository,
        IAssessmentPolicyService policyService,
        IAuditService auditService)
    {
        _repository = repository;
        _policyService = policyService;
        _auditService = auditService;
    }

    public async Task<AssessmentRunSubmissionResult> SubmitAsync(AssessmentRunSubmissionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTime.UtcNow;
        var evaluation = _policyService.Evaluate(new AssessmentPolicyEvaluationRequest
        {
            ToolId = request.ToolId,
            RequestedBy = request.RequestedBy,
            TargetScope = request.TargetScope,
            ScopeKind = request.ScopeKind,
            Environment = request.Environment,
            ApprovalReference = request.ApprovalReference,
            DryRun = request.DryRun,
            ScopeTags = request.ScopeTags
        });

        var status = evaluation.Decision switch
        {
            AssessmentPolicyDecision.Allowed => AssessmentRunStatus.Queued,
            AssessmentPolicyDecision.RequiresApproval => AssessmentRunStatus.AwaitingApproval,
            _ => AssessmentRunStatus.Denied
        };

        var run = new AssessmentRun
        {
            ToolId = request.ToolId,
            RequestedBy = request.RequestedBy,
            TargetScope = request.TargetScope,
            ScopeKind = request.ScopeKind,
            Environment = request.Environment,
            ParametersJson = string.IsNullOrWhiteSpace(request.ParametersJson) ? "{}" : request.ParametersJson,
            PolicyDecisionJson = JsonSerializer.Serialize(evaluation, SerializerOptions),
            ApprovalReference = request.ApprovalReference,
            DryRun = request.DryRun,
            RequiresApproval = evaluation.RequiresApproval,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
            SubmittedAt = now
        };

        await _repository.CreateAsync(run, cancellationToken);

        await _auditService.WriteAsync(
            actor: string.IsNullOrWhiteSpace(request.RequestedBy) ? "assessment-run-service" : request.RequestedBy,
            action: ResolveAuditAction(status),
            entityType: nameof(AssessmentRun),
            entityId: run.RunId,
            newValue: new
            {
                run.RunId,
                run.ToolId,
                run.RequestedBy,
                run.TargetScope,
                run.Environment,
                run.Status,
                run.RequiresApproval,
                run.ApprovalReference
            },
            metadata: evaluation,
            cancellationToken: cancellationToken);

        return new AssessmentRunSubmissionResult
        {
            Run = run,
            PolicyEvaluation = evaluation
        };
    }

    private static string ResolveAuditAction(AssessmentRunStatus status)
        => status switch
        {
            AssessmentRunStatus.Queued => "assessment-run-queued",
            AssessmentRunStatus.AwaitingApproval => "assessment-run-awaiting-approval",
            AssessmentRunStatus.Denied => "assessment-run-denied",
            _ => "assessment-run-submitted"
        };
}
