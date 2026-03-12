using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Core.Services;

public sealed class DefaultAssessmentToolCatalog : IAssessmentToolCatalog
{
    private readonly IReadOnlyList<AssessmentToolDefinition> _definitions;
    private readonly IReadOnlyDictionary<string, AssessmentToolDefinition> _definitionsById;

    public DefaultAssessmentToolCatalog()
    {
        var definitions = BuildDefinitions()
            .OrderBy(definition => definition.ToolId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ValidateDefinitions(definitions);

        _definitions = definitions;
        _definitionsById = definitions.ToDictionary(definition => definition.ToolId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<AssessmentToolDefinition> GetAll() => _definitions;

    public AssessmentToolDefinition? GetById(string toolId)
        => string.IsNullOrWhiteSpace(toolId)
            ? null
            : _definitionsById.GetValueOrDefault(toolId);

    public bool IsRegistered(string toolId)
        => !string.IsNullOrWhiteSpace(toolId) && _definitionsById.ContainsKey(toolId);

    private static AssessmentToolDefinition[] BuildDefinitions()
    {
        return
        [
            new()
            {
                ToolId = "asset.discovery.reconcile",
                DisplayName = "Asset discovery reconcile",
                Category = "inventory",
                Description = "Schedules discovery reconciliation for an approved target scope and records inventory deltas.",
                ExecutionMode = AssessmentToolExecutionMode.ActiveValidation,
                RiskLevel = AssessmentToolRiskLevel.Moderate,
                ExternalUseAllowed = true,
                LabUseAllowed = true,
                RequiredTags = ["authorized-scope", "inventory"],
                SafetyChecks = ["scope-allowlist", "rate-limit", "approval-ticket", "audit-log"],
                Parameters =
                [
                    new() { Name = "targetScope", Type = "string", Description = "IP, CIDR, or application scope identifier.", Required = true },
                    new() { Name = "scanProfile", Type = "string", Description = "Approved discovery profile.", Required = true, AllowedValues = ["standard", "high-signal"] }
                ]
            },
            new()
            {
                ToolId = "evidence.bundle.create",
                DisplayName = "Evidence bundle create",
                Category = "evidence",
                Description = "Creates a normalized evidence package from completed observations, enrichments, and findings.",
                ExecutionMode = AssessmentToolExecutionMode.Passive,
                RiskLevel = AssessmentToolRiskLevel.Low,
                ExternalUseAllowed = true,
                LabUseAllowed = true,
                RequiredTags = ["evidence"],
                SafetyChecks = ["audit-log", "retention-policy"],
                Parameters =
                [
                    new() { Name = "assessmentRunId", Type = "string", Description = "Logical assessment run identifier.", Required = true },
                    new() { Name = "format", Type = "string", Description = "Output format for the evidence bundle.", Required = true, AllowedValues = ["json", "markdown"] }
                ]
            },
            new()
            {
                ToolId = "exposure.query.current",
                DisplayName = "Current exposure query",
                Category = "query",
                Description = "Queries current exposure state for an authorized target, service, or application workflow.",
                ExecutionMode = AssessmentToolExecutionMode.Passive,
                RiskLevel = AssessmentToolRiskLevel.Low,
                ExternalUseAllowed = true,
                LabUseAllowed = true,
                RequiredTags = ["authorized-scope", "read-only"],
                SafetyChecks = ["scope-allowlist", "audit-log"],
                Parameters =
                [
                    new() { Name = "query", Type = "string", Description = "Query DSL expression or lookup key.", Required = true }
                ]
            },
            new()
            {
                ToolId = "service.http.metadata.collect",
                DisplayName = "HTTP metadata collect",
                Category = "service-validation",
                Description = "Collects non-invasive HTTP metadata such as status code, title, and server header for an approved endpoint.",
                ExecutionMode = AssessmentToolExecutionMode.ActiveValidation,
                RiskLevel = AssessmentToolRiskLevel.Moderate,
                ExternalUseAllowed = true,
                LabUseAllowed = true,
                RequiredTags = ["authorized-scope", "http"],
                SafetyChecks = ["scope-allowlist", "rate-limit", "approval-ticket", "audit-log"],
                Parameters =
                [
                    new() { Name = "target", Type = "string", Description = "Hostname or IP address.", Required = true },
                    new() { Name = "port", Type = "integer", Description = "Target port.", Required = true },
                    new() { Name = "path", Type = "string", Description = "HTTP path to collect metadata from.", Required = false }
                ]
            },
            new()
            {
                ToolId = "service.tls.certificate.collect",
                DisplayName = "TLS certificate collect",
                Category = "service-validation",
                Description = "Collects certificate metadata from an approved TLS endpoint for evidence and expiry analysis.",
                ExecutionMode = AssessmentToolExecutionMode.ActiveValidation,
                RiskLevel = AssessmentToolRiskLevel.Moderate,
                ExternalUseAllowed = true,
                LabUseAllowed = true,
                RequiredTags = ["authorized-scope", "tls"],
                SafetyChecks = ["scope-allowlist", "rate-limit", "approval-ticket", "audit-log"],
                Parameters =
                [
                    new() { Name = "target", Type = "string", Description = "Hostname or IP address.", Required = true },
                    new() { Name = "port", Type = "integer", Description = "TLS port.", Required = true }
                ]
            },
            new()
            {
                ToolId = "validation.workflow.dry-run",
                DisplayName = "Workflow dry run",
                Category = "workflow",
                Description = "Validates assessment workflow inputs and policy gates without performing any live network action.",
                ExecutionMode = AssessmentToolExecutionMode.DryRunOnly,
                RiskLevel = AssessmentToolRiskLevel.Low,
                ExternalUseAllowed = true,
                LabUseAllowed = true,
                ProducesEvidence = false,
                RequiredTags = ["preflight"],
                SafetyChecks = ["policy-check", "scope-allowlist", "audit-log"],
                Parameters =
                [
                    new() { Name = "workflowName", Type = "string", Description = "Workflow identifier to validate.", Required = true },
                    new() { Name = "targetScope", Type = "string", Description = "Target scope under review.", Required = true }
                ]
            },
            new()
            {
                ToolId = "vulnerability.cve.correlate",
                DisplayName = "CVE correlate",
                Category = "enrichment",
                Description = "Correlates service fingerprints and versions with known CVE data for triage and reporting.",
                ExecutionMode = AssessmentToolExecutionMode.Passive,
                RiskLevel = AssessmentToolRiskLevel.Low,
                ExternalUseAllowed = true,
                LabUseAllowed = true,
                RequiredTags = ["read-only", "cve"],
                SafetyChecks = ["audit-log", "evidence-linkage"],
                Parameters =
                [
                    new() { Name = "serviceName", Type = "string", Description = "Observed service name.", Required = true },
                    new() { Name = "serviceVersion", Type = "string", Description = "Observed or inferred service version.", Required = false }
                ]
            }
        ];
    }

    private static void ValidateDefinitions(IReadOnlyCollection<AssessmentToolDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        if (definitions.Count == 0)
            throw new InvalidOperationException("At least one assessment tool definition is required.");

        var duplicateIds = definitions
            .GroupBy(definition => definition.ToolId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateIds.Length > 0)
            throw new InvalidOperationException($"Duplicate assessment tool ids detected: {string.Join(", ", duplicateIds)}");

        foreach (var definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.ToolId))
                throw new InvalidOperationException("Assessment tool id cannot be empty.");

            if (string.IsNullOrWhiteSpace(definition.DisplayName))
                throw new InvalidOperationException($"Assessment tool '{definition.ToolId}' is missing a display name.");

            if (string.IsNullOrWhiteSpace(definition.Category))
                throw new InvalidOperationException($"Assessment tool '{definition.ToolId}' is missing a category.");

            if (definition.ExternalUseAllowed && !definition.RequiresApprovalForExternalTargets)
                throw new InvalidOperationException($"Assessment tool '{definition.ToolId}' must require approval for external targets.");

            if (definition.AllowsStateChangingActions)
                throw new InvalidOperationException($"Assessment tool '{definition.ToolId}' cannot allow state-changing actions in the default catalog.");

            if (definition.AllowsBinaryPayloads)
                throw new InvalidOperationException($"Assessment tool '{definition.ToolId}' cannot allow binary payloads in the default catalog.");

            if (definition.AllowsRemoteCodeExecution)
                throw new InvalidOperationException($"Assessment tool '{definition.ToolId}' cannot allow remote code execution in the default catalog.");

            if (definition.SafetyChecks.Length == 0)
                throw new InvalidOperationException($"Assessment tool '{definition.ToolId}' must declare at least one safety check.");
        }
    }
}
