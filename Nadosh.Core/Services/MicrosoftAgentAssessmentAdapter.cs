using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Core.Services;

public sealed class MicrosoftAgentAssessmentAdapter : IMicrosoftAgentAssessmentAdapter
{
    private readonly IAssessmentRunRepository _runRepository;
    private readonly IAssessmentEvidenceService _evidenceService;
    private readonly IAssessmentToolCatalog _toolCatalog;

    public MicrosoftAgentAssessmentAdapter(
        IAssessmentRunRepository runRepository,
        IAssessmentEvidenceService evidenceService,
        IAssessmentToolCatalog toolCatalog)
    {
        _runRepository = runRepository;
        _evidenceService = evidenceService;
        _toolCatalog = toolCatalog;
    }

    public async Task<MicrosoftAgentAssessmentContext?> BuildContextAsync(string runId, CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(runId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        var evidenceBundle = await _evidenceService.BuildAsync(runId, cancellationToken);
        var tools = _toolCatalog.GetAll()
            .OrderBy(tool => tool.ToolId, StringComparer.OrdinalIgnoreCase)
            .Select(tool => BuildToolManifest(tool, run))
            .ToArray();

        return new MicrosoftAgentAssessmentContext
        {
            SessionId = $"assessment-run:{run.RunId}",
            Run = run,
            EvidenceBundle = evidenceBundle,
            SystemInstructions = BuildSystemInstructions(run),
            WorkflowHints = BuildWorkflowHints(run, evidenceBundle),
            Tools = tools
        };
    }

    private static MicrosoftAgentToolManifest BuildToolManifest(AssessmentToolDefinition definition, AssessmentRun run)
    {
        var blockedReasons = new List<string>();
        var availability = ResolveAvailability(definition, run, blockedReasons);

        return new MicrosoftAgentToolManifest
        {
            ToolId = definition.ToolId,
            DisplayName = definition.DisplayName,
            Category = definition.Category,
            Description = definition.Description,
            ExecutionMode = definition.ExecutionMode,
            RiskLevel = definition.RiskLevel,
            IsPrimaryTool = string.Equals(definition.ToolId, run.ToolId, StringComparison.OrdinalIgnoreCase),
            CanExecuteNow = availability == MicrosoftAgentToolAvailability.Ready,
            Availability = availability,
            SafetyChecks = definition.SafetyChecks,
            RequiredTags = definition.RequiredTags,
            BlockedReasons = blockedReasons.ToArray(),
            Parameters = definition.Parameters
        };
    }

    private static MicrosoftAgentToolAvailability ResolveAvailability(
        AssessmentToolDefinition definition,
        AssessmentRun run,
        ICollection<string> blockedReasons)
    {
        if (!definition.EnabledByDefault)
        {
            blockedReasons.Add("tool-disabled-by-default");
            return MicrosoftAgentToolAvailability.Disabled;
        }

        if (run.Environment == AssessmentExecutionEnvironment.ExternalAuthorized && !definition.ExternalUseAllowed)
        {
            blockedReasons.Add("tool-not-approved-for-external-targets");
            return MicrosoftAgentToolAvailability.EnvironmentBlocked;
        }

        if (run.Environment == AssessmentExecutionEnvironment.Lab && !definition.LabUseAllowed)
        {
            blockedReasons.Add("tool-not-approved-for-lab-targets");
            return MicrosoftAgentToolAvailability.EnvironmentBlocked;
        }

        if (run.Environment == AssessmentExecutionEnvironment.ExternalAuthorized
            && definition.RequiresApprovalForExternalTargets
            && string.IsNullOrWhiteSpace(run.ApprovalReference))
        {
            blockedReasons.Add("approval-reference-required");
            return MicrosoftAgentToolAvailability.ApprovalRequired;
        }

        if (definition.ExecutionMode == AssessmentToolExecutionMode.ActiveValidation)
        {
            blockedReasons.Add("active-validation-execution-not-implemented");
            return MicrosoftAgentToolAvailability.ExecutionAdapterPlanned;
        }

        return MicrosoftAgentToolAvailability.Ready;
    }

    private static string BuildSystemInstructions(AssessmentRun run)
    {
        return string.Join('\n',
        [
            "You are a Microsoft Agent Framework assessment planner operating inside Nadosh.",
            $"Stay strictly within the authorized scope: {run.TargetScope} ({run.ScopeKind}).",
            $"Operate only in the approved environment: {run.Environment}.",
            "Use only the tools listed in this context. Do not invent new tools or actions.",
            "Do not perform exploit automation, payload generation, remote code execution, or command-and-control behavior.",
            "Prefer passive evidence collection and dry-run planning unless a listed tool is explicitly ready and approved.",
            "When a tool is marked as approval-required, environment-blocked, disabled, or execution-adapter-planned, do not execute it.",
            "Summarize findings with evidence references and note any policy or execution limitations clearly."
        ]);
    }

    private static IReadOnlyCollection<string> BuildWorkflowHints(AssessmentRun run, AssessmentEvidenceBundle? evidenceBundle)
    {
        var hints = new List<string>
        {
            $"Primary tool for this run: {run.ToolId}",
            $"Run status at context generation time: {run.Status}",
            $"Scope kind: {run.ScopeKind}",
            $"Dry-run requested: {run.DryRun}"
        };

        if (!string.IsNullOrWhiteSpace(run.ApprovalReference))
        {
            hints.Add($"Approval reference attached: {run.ApprovalReference}");
        }

        if (evidenceBundle is not null)
        {
            hints.Add($"Evidence bundle contains {evidenceBundle.ExposureCount} exposures, {evidenceBundle.ObservationCount} observations, and {evidenceBundle.EnrichmentCount} enrichments.");

            foreach (var note in evidenceBundle.Notes.Take(3))
            {
                hints.Add($"Evidence note: {note}");
            }
        }
        else
        {
            hints.Add("No evidence bundle was available when the context was built.");
        }

        return hints;
    }
}
