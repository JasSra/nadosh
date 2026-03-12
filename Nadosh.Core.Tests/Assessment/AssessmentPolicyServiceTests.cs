using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;
using Nadosh.Core.Services;

namespace Nadosh.Core.Tests.Assessment;

public sealed class AssessmentPolicyServiceTests
{
    private readonly IAssessmentPolicyService _service = new AssessmentPolicyService(new DefaultAssessmentToolCatalog());

    [Fact]
    public void Evaluate_AllowsLabDryRunWorkflowValidation()
    {
        var result = _service.Evaluate(new AssessmentPolicyEvaluationRequest
        {
            ToolId = "validation.workflow.dry-run",
            RequestedBy = "analyst@example.com",
            TargetScope = "lab/web-portal",
            ScopeKind = AssessmentScopeKind.Workflow,
            Environment = AssessmentExecutionEnvironment.Lab,
            DryRun = true,
            ScopeTags = ["preflight"]
        });

        Assert.True(result.IsAllowed);
        Assert.Equal(AssessmentPolicyDecision.Allowed, result.Decision);
        Assert.Equal("validation.workflow.dry-run", result.ToolId);
    }

    [Fact]
    public void Evaluate_RequiresApproval_ForExternalActiveValidation()
    {
        var result = _service.Evaluate(new AssessmentPolicyEvaluationRequest
        {
            ToolId = "service.tls.certificate.collect",
            RequestedBy = "operator@example.com",
            TargetScope = "203.0.113.10:443",
            ScopeKind = AssessmentScopeKind.Service,
            Environment = AssessmentExecutionEnvironment.ExternalAuthorized,
            ScopeTags = ["authorized-scope", "tls"]
        });

        Assert.False(result.IsAllowed);
        Assert.True(result.RequiresApproval);
        Assert.Equal(AssessmentPolicyDecision.RequiresApproval, result.Decision);
        Assert.Contains("approval-reference-required", result.Reasons);
    }

    [Fact]
    public void Evaluate_DeniesBroadWildcardScope()
    {
        var result = _service.Evaluate(new AssessmentPolicyEvaluationRequest
        {
            ToolId = "exposure.query.current",
            RequestedBy = "operator@example.com",
            TargetScope = "0.0.0.0/0",
            ScopeKind = AssessmentScopeKind.Cidr,
            Environment = AssessmentExecutionEnvironment.InternalAuthorized,
            ScopeTags = ["authorized-scope", "read-only"]
        });

        Assert.False(result.IsAllowed);
        Assert.Equal(AssessmentPolicyDecision.Denied, result.Decision);
        Assert.Contains("scope-too-broad", result.Reasons);
    }

    [Fact]
    public void Evaluate_DeniesWhenRequiredScopeTagsAreMissing()
    {
        var result = _service.Evaluate(new AssessmentPolicyEvaluationRequest
        {
            ToolId = "service.http.metadata.collect",
            RequestedBy = "operator@example.com",
            TargetScope = "198.51.100.25:8080",
            ScopeKind = AssessmentScopeKind.Service,
            Environment = AssessmentExecutionEnvironment.InternalAuthorized,
            ScopeTags = ["http"]
        });

        Assert.False(result.IsAllowed);
        Assert.Equal(AssessmentPolicyDecision.Denied, result.Decision);
        Assert.Contains("authorized-scope", result.MissingRequirements);
    }

    [Fact]
    public void Evaluate_AllowsApprovedExternalRequest()
    {
        var result = _service.Evaluate(new AssessmentPolicyEvaluationRequest
        {
            ToolId = "service.http.metadata.collect",
            RequestedBy = "operator@example.com",
            TargetScope = "198.51.100.25:8080",
            ScopeKind = AssessmentScopeKind.Service,
            Environment = AssessmentExecutionEnvironment.ExternalAuthorized,
            ApprovalReference = "CHG-12345",
            ScopeTags = ["authorized-scope", "http"]
        });

        Assert.True(result.IsAllowed);
        Assert.True(result.RequiresApproval);
        Assert.Equal(AssessmentPolicyDecision.Allowed, result.Decision);
    }
}
