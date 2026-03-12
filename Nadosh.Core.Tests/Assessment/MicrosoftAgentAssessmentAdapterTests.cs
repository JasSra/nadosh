using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;
using Nadosh.Core.Services;

namespace Nadosh.Core.Tests.Assessment;

public sealed class MicrosoftAgentAssessmentAdapterTests
{
    [Fact]
    public async Task BuildContextAsync_ReturnsNull_ForUnknownRun()
    {
        var adapter = CreateAdapter();

        var result = await adapter.BuildContextAsync("missing-run");

        Assert.Null(result);
    }

    [Fact]
    public async Task BuildContextAsync_IncludesPrimaryToolAndInstructions()
    {
        var run = new AssessmentRun
        {
            RunId = "run-1",
            ToolId = "validation.workflow.dry-run",
            RequestedBy = "analyst@example.com",
            TargetScope = "lab/api",
            ScopeKind = AssessmentScopeKind.Workflow,
            Environment = AssessmentExecutionEnvironment.Lab,
            DryRun = true,
            Status = AssessmentRunStatus.Queued
        };

        var adapter = CreateAdapter(run: run, bundle: new AssessmentEvidenceBundle
        {
            Run = run,
            Notes = ["workflow evidence note"]
        });

        var result = await adapter.BuildContextAsync(run.RunId);

        Assert.NotNull(result);
        Assert.Contains("Stay strictly within the authorized scope", result!.SystemInstructions);
        Assert.Contains(result.Tools, tool => tool.IsPrimaryTool && tool.ToolId == run.ToolId);
        Assert.Contains(result.WorkflowHints, hint => hint.Contains("workflow evidence note", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildContextAsync_MarksActiveValidationAsPlanned()
    {
        var run = new AssessmentRun
        {
            RunId = "run-2",
            ToolId = "service.tls.certificate.collect",
            RequestedBy = "operator@example.com",
            TargetScope = "203.0.113.10:443",
            ScopeKind = AssessmentScopeKind.Service,
            Environment = AssessmentExecutionEnvironment.ExternalAuthorized,
            ApprovalReference = "CHG-12345",
            Status = AssessmentRunStatus.Queued
        };

        var adapter = CreateAdapter(run: run, bundle: new AssessmentEvidenceBundle { Run = run });

        var result = await adapter.BuildContextAsync(run.RunId);
        var primaryTool = Assert.Single(result!.Tools, tool => tool.IsPrimaryTool);

        Assert.Equal(MicrosoftAgentToolAvailability.ExecutionAdapterPlanned, primaryTool.Availability);
        Assert.False(primaryTool.CanExecuteNow);
        Assert.Contains("active-validation-execution-not-implemented", primaryTool.BlockedReasons);
    }

    [Fact]
    public async Task BuildContextAsync_MarksMissingApproval()
    {
        var run = new AssessmentRun
        {
            RunId = "run-3",
            ToolId = "service.http.metadata.collect",
            RequestedBy = "operator@example.com",
            TargetScope = "198.51.100.25:8080",
            ScopeKind = AssessmentScopeKind.Service,
            Environment = AssessmentExecutionEnvironment.ExternalAuthorized,
            Status = AssessmentRunStatus.AwaitingApproval
        };

        var adapter = CreateAdapter(run: run, bundle: new AssessmentEvidenceBundle { Run = run });

        var result = await adapter.BuildContextAsync(run.RunId);
        var primaryTool = Assert.Single(result!.Tools, tool => tool.IsPrimaryTool);

        Assert.Equal(MicrosoftAgentToolAvailability.ApprovalRequired, primaryTool.Availability);
        Assert.Contains("approval-reference-required", primaryTool.BlockedReasons);
    }

    private static IMicrosoftAgentAssessmentAdapter CreateAdapter(AssessmentRun? run = null, AssessmentEvidenceBundle? bundle = null)
    {
        return new MicrosoftAgentAssessmentAdapter(
            new FakeAssessmentRunRepository(run),
            new FakeAssessmentEvidenceService(bundle),
            new DefaultAssessmentToolCatalog());
    }

    private sealed class FakeAssessmentRunRepository : IAssessmentRunRepository
    {
        private readonly AssessmentRun? _run;

        public FakeAssessmentRunRepository(AssessmentRun? run)
        {
            _run = run;
        }

        public Task<AssessmentRun?> GetByIdAsync(string runId, CancellationToken cancellationToken = default)
            => Task.FromResult(_run is not null && _run.RunId == runId ? _run : null);

        public Task<IReadOnlyList<AssessmentRun>> GetByStatusAsync(AssessmentRunStatus status, int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AssessmentRun>>(_run is not null && _run.Status == status ? [_run] : Array.Empty<AssessmentRun>());

        public Task CreateAsync(AssessmentRun run, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(AssessmentRun run, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeAssessmentEvidenceService : IAssessmentEvidenceService
    {
        private readonly AssessmentEvidenceBundle? _bundle;

        public FakeAssessmentEvidenceService(AssessmentEvidenceBundle? bundle)
        {
            _bundle = bundle;
        }

        public Task<AssessmentEvidenceBundle?> BuildAsync(string runId, CancellationToken cancellationToken = default)
            => Task.FromResult(_bundle);
    }
}
