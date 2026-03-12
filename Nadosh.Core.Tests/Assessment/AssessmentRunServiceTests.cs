using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;
using Nadosh.Core.Services;

namespace Nadosh.Core.Tests.Assessment;

public sealed class AssessmentRunServiceTests
{
    [Fact]
    public async Task SubmitAsync_QueuesAllowedRun()
    {
        var repository = new FakeAssessmentRunRepository();
        var auditService = new FakeAuditService();
        var service = new AssessmentRunService(
            repository,
            new AssessmentPolicyService(new DefaultAssessmentToolCatalog()),
            auditService);

        var result = await service.SubmitAsync(new AssessmentRunSubmissionRequest
        {
            ToolId = "service.http.metadata.collect",
            RequestedBy = "operator@example.com",
            TargetScope = "198.51.100.25:8080",
            ScopeKind = AssessmentScopeKind.Service,
            Environment = AssessmentExecutionEnvironment.ExternalAuthorized,
            ApprovalReference = "CHG-12345",
            ScopeTags = ["authorized-scope", "http"]
        });

        Assert.Equal(AssessmentRunStatus.Queued, result.Run.Status);
        Assert.Single(repository.CreatedRuns);
        Assert.Single(auditService.Events);
        Assert.Equal("assessment-run-queued", auditService.Events[0].Action);
    }

    [Fact]
    public async Task SubmitAsync_ParksRunAwaitingApproval_WhenReferenceMissing()
    {
        var repository = new FakeAssessmentRunRepository();
        var auditService = new FakeAuditService();
        var service = new AssessmentRunService(
            repository,
            new AssessmentPolicyService(new DefaultAssessmentToolCatalog()),
            auditService);

        var result = await service.SubmitAsync(new AssessmentRunSubmissionRequest
        {
            ToolId = "service.tls.certificate.collect",
            RequestedBy = "operator@example.com",
            TargetScope = "203.0.113.10:443",
            ScopeKind = AssessmentScopeKind.Service,
            Environment = AssessmentExecutionEnvironment.ExternalAuthorized,
            ScopeTags = ["authorized-scope", "tls"]
        });

        Assert.Equal(AssessmentRunStatus.AwaitingApproval, result.Run.Status);
        Assert.True(result.Run.RequiresApproval);
        Assert.Equal("assessment-run-awaiting-approval", auditService.Events[0].Action);
    }

    [Fact]
    public async Task SubmitAsync_DeniesUnknownTool()
    {
        var repository = new FakeAssessmentRunRepository();
        var auditService = new FakeAuditService();
        var service = new AssessmentRunService(
            repository,
            new AssessmentPolicyService(new DefaultAssessmentToolCatalog()),
            auditService);

        var result = await service.SubmitAsync(new AssessmentRunSubmissionRequest
        {
            ToolId = "unknown.tool",
            RequestedBy = "operator@example.com",
            TargetScope = "198.51.100.1",
            ScopeKind = AssessmentScopeKind.IpAddress,
            Environment = AssessmentExecutionEnvironment.Lab
        });

        Assert.Equal(AssessmentRunStatus.Denied, result.Run.Status);
        Assert.Contains("tool-not-registered", result.PolicyEvaluation.Reasons);
        Assert.Equal("assessment-run-denied", auditService.Events[0].Action);
    }

    private sealed class FakeAssessmentRunRepository : IAssessmentRunRepository
    {
        public List<AssessmentRun> CreatedRuns { get; } = [];

        public Task<AssessmentRun?> GetByIdAsync(string runId, CancellationToken cancellationToken = default)
            => Task.FromResult(CreatedRuns.FirstOrDefault(run => run.RunId == runId));

        public Task<IReadOnlyList<AssessmentRun>> GetByStatusAsync(AssessmentRunStatus status, int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AssessmentRun>>(CreatedRuns.Where(run => run.Status == status).Take(take).ToArray());

        public Task CreateAsync(AssessmentRun run, CancellationToken cancellationToken = default)
        {
            CreatedRuns.Add(run);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(AssessmentRun run, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeAuditService : IAuditService
    {
        public List<FakeAuditEvent> Events { get; } = [];

        public Task WriteAsync(string actor, string action, string entityType, string entityId, object? oldValue = null, object? newValue = null, object? metadata = null, CancellationToken cancellationToken = default)
        {
            Events.Add(new FakeAuditEvent(actor, action, entityType, entityId));
            return Task.CompletedTask;
        }
    }

    private sealed record FakeAuditEvent(string Actor, string Action, string EntityType, string EntityId);
}
