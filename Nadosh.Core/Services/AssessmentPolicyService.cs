using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Core.Services;

public sealed class AssessmentPolicyService : IAssessmentPolicyService
{
    private static readonly string[] BroadScopeMarkers = ["*", "any", "all", "0.0.0.0/0", "::/0"];
    private readonly IAssessmentToolCatalog _toolCatalog;

    public AssessmentPolicyService(IAssessmentToolCatalog toolCatalog)
    {
        _toolCatalog = toolCatalog;
    }

    public AssessmentPolicyEvaluation Evaluate(AssessmentPolicyEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reasons = new List<string>();
        var missingRequirements = new List<string>();

        if (string.IsNullOrWhiteSpace(request.ToolId))
            missingRequirements.Add("toolId");

        if (string.IsNullOrWhiteSpace(request.RequestedBy))
            missingRequirements.Add("requestedBy");

        if (string.IsNullOrWhiteSpace(request.TargetScope))
            missingRequirements.Add("targetScope");

        if (missingRequirements.Count > 0)
        {
            reasons.Add("request-missing-required-fields");
            return BuildEvaluation(
                AssessmentPolicyDecision.Denied,
                toolId: request.ToolId,
                reasons: reasons,
                missingRequirements: missingRequirements);
        }

        var definition = _toolCatalog.GetById(request.ToolId);
        if (definition is null)
        {
            reasons.Add("tool-not-registered");
            return BuildEvaluation(
                AssessmentPolicyDecision.Denied,
                toolId: request.ToolId,
                reasons: reasons,
                missingRequirements: missingRequirements);
        }

        if (IsBroadScope(request.TargetScope))
        {
            reasons.Add("scope-too-broad");
        }

        if (request.Environment == AssessmentExecutionEnvironment.ExternalAuthorized && !definition.ExternalUseAllowed)
        {
            reasons.Add("tool-not-approved-for-external-targets");
        }

        if (request.Environment == AssessmentExecutionEnvironment.Lab && !definition.LabUseAllowed)
        {
            reasons.Add("tool-not-approved-for-lab-targets");
        }

        var normalizedScopeTags = request.ScopeTags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var requiredTag in definition.RequiredTags)
        {
            if (!normalizedScopeTags.Contains(requiredTag))
            {
                missingRequirements.Add(requiredTag);
            }
        }

        if (missingRequirements.Count > 0)
        {
            reasons.Add("missing-required-scope-tags");
        }

        var requiresApproval = request.Environment == AssessmentExecutionEnvironment.ExternalAuthorized
            && definition.RequiresApprovalForExternalTargets;

        if (requiresApproval && string.IsNullOrWhiteSpace(request.ApprovalReference))
        {
            reasons.Add("approval-reference-required");
            return BuildEvaluation(
                AssessmentPolicyDecision.RequiresApproval,
                definition.ToolId,
                reasons,
                missingRequirements,
                definition.SafetyChecks,
                requiresApproval: true);
        }

        if (reasons.Count > 0)
        {
            return BuildEvaluation(
                AssessmentPolicyDecision.Denied,
                definition.ToolId,
                reasons,
                missingRequirements,
                definition.SafetyChecks,
                requiresApproval: requiresApproval);
        }

        return BuildEvaluation(
            AssessmentPolicyDecision.Allowed,
            definition.ToolId,
            reasons,
            missingRequirements,
            definition.SafetyChecks,
            requiresApproval: requiresApproval);
    }

    private static bool IsBroadScope(string targetScope)
        => BroadScopeMarkers.Contains(targetScope.Trim(), StringComparer.OrdinalIgnoreCase);

    private static AssessmentPolicyEvaluation BuildEvaluation(
        AssessmentPolicyDecision decision,
        string toolId,
        IEnumerable<string> reasons,
        IEnumerable<string> missingRequirements,
        IEnumerable<string>? appliedSafetyChecks = null,
        bool requiresApproval = false)
    {
        return new AssessmentPolicyEvaluation
        {
            Decision = decision,
            IsAllowed = decision == AssessmentPolicyDecision.Allowed,
            RequiresApproval = requiresApproval,
            ToolId = toolId,
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            MissingRequirements = missingRequirements.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            AppliedSafetyChecks = appliedSafetyChecks?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? []
        };
    }
}
