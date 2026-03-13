using Microsoft.Extensions.Logging;
using Nadosh.Agents.Agents;
using Nadosh.Agents.Models;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Agents.Orchestration;

/// <summary>
/// Orchestrates multi-phase penetration testing workflow using AI agents.
/// Manages phase progression: Recon → Enumeration → Prioritization → Validation → Reporting
/// </summary>
public class PhaseOrchestrationEngine
{
    private readonly ILogger<PhaseOrchestrationEngine> _logger;
    private readonly GetCapabilitiesToolsAgent _capabilitiesAgent;
    private readonly ExecuteCommandAgent _executeAgent;
    private readonly ParseAndPlanAgent _planAgent;
    private readonly IAssessmentRunRepository _assessmentRepository;
    private readonly IAssessmentEvidenceService _evidenceService;

    public PhaseOrchestrationEngine(
        ILogger<PhaseOrchestrationEngine> logger,
        GetCapabilitiesToolsAgent capabilitiesAgent,
        ExecuteCommandAgent executeAgent,
        ParseAndPlanAgent planAgent,
        IAssessmentRunRepository assessmentRepository,
        IAssessmentEvidenceService evidenceService)
    {
        _logger = logger;
        _capabilitiesAgent = capabilitiesAgent;
        _executeAgent = executeAgent;
        _planAgent = planAgent;
        _assessmentRepository = assessmentRepository;
        _evidenceService = evidenceService;
    }

    /// <summary>
    /// Executes a complete assessment run through all phases.
    /// </summary>
    public async Task<AssessmentRunResult> ExecuteAssessmentAsync(
        string assessmentRunId,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Starting assessment run orchestration: {RunId}", assessmentRunId);

        try
        {
            // Load assessment run from repository
            var assessmentRun = await _assessmentRepository.GetByIdAsync(assessmentRunId, ct);
            if (assessmentRun == null)
            {
                throw new InvalidOperationException($"Assessment run {assessmentRunId} not found");
            }

            // Discover available tools with category inventory
            var availableTools = await _capabilitiesAgent.DiscoverToolsAsync(ct);
            var categoryInventory = await _capabilitiesAgent.GetCategoryInventoryAsync(ct);
            
            _logger.LogInformation("Discovered {Count} pentest tools across {Categories} categories", 
                availableTools.Count, categoryInventory.Count);
            
            foreach (var category in categoryInventory.OrderByDescending(kvp => kvp.Value))
            {
                _logger.LogInformation("  - {Category}: {Count} tools", category.Key, category.Value);
            }

            // Initialize phase context
            var context = new AgentPlanningContext
            {
                AssessmentRunId = assessmentRunId,
                CurrentPhase = AssessmentPhase.Recon,
                TargetScope = assessmentRun.TargetScope,
                AvailableTools = availableTools,
                PhaseStartedAt = DateTime.UtcNow,
                MaxIterations = 10,
                CurrentIteration = 0
            };

            context.PhaseGoals = GetPhaseGoals(context.CurrentPhase, assessmentRun);

            var allFindings = new List<Finding>();
            var allCommandResults = new List<CommandExecutionResult>();

            // Execute phases sequentially
            foreach (var phase in GetPhaseSequence())
            {
                if (ct.IsCancellationRequested) break;

                context.CurrentPhase = phase;
                context.PhaseStartedAt = DateTime.UtcNow;
                context.CurrentIteration = 0;
                context.PhaseGoals = GetPhaseGoals(phase, assessmentRun);

                _logger.LogInformation("Starting phase: {Phase}", phase);

                // Execute phase iterations
                var phaseResult = await ExecutePhaseAsync(context, ct);
                
                allFindings.AddRange(phaseResult.Findings);
                allCommandResults.AddRange(phaseResult.CommandResults);

                // Update context for next phase
                context.PreviousResults = allCommandResults;
                context.CurrentFindings = allFindings;

                _logger.LogInformation("Phase {Phase} completed: {FindingCount} findings, {CommandCount} commands",
                    phase, phaseResult.Findings.Count, phaseResult.CommandResults.Count);

                // Check if we should advance
                if (!phaseResult.ShouldAdvance)
                {
                    _logger.LogWarning("Phase {Phase} indicated not to advance. Stopping assessment.", phase);
                    break;
                }
            }

            // Generate final evidence bundle
            AssessmentEvidenceBundle? evidence = null;
            
            try
            {
                // Try to build evidence for the assessment run
                evidence = await _evidenceService.BuildAsync(assessmentRunId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build evidence bundle for {RunId}", assessmentRunId);
            }

            return new AssessmentRunResult
            {
                AssessmentRunId = assessmentRunId,
                Success = true,
                Findings = allFindings,
                CommandResults = allCommandResults,
                EvidenceBundle = evidence
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Assessment run failed: {RunId}", assessmentRunId);
            
            return new AssessmentRunResult
            {
                AssessmentRunId = assessmentRunId,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Executes a single assessment phase with AI-driven iteration.
    /// </summary>
    private async Task<PhaseResult> ExecutePhaseAsync(
        AgentPlanningContext context,
        CancellationToken ct)
    {
        var phaseFindings = new List<Finding>();
        var phaseCommands = new List<CommandExecutionResult>();

        // Get phase-specific tools
        var phaseTools = await _capabilitiesAgent.GetToolsForPhaseAsync(context.CurrentPhase, ct);
        context.AvailableTools = phaseTools;

        while (context.CurrentIteration < context.MaxIterations && !ct.IsCancellationRequested)
        {
            context.CurrentIteration++;

            // Ask AI to parse results and plan next steps
            var plan = await _planAgent.ParseAndPlanAsync(context, ct);

            _logger.LogInformation("Phase {Phase} iteration {Iteration}: plan generated - continue={Continue}, advance={Advance}, commands={CommandCount}",
                context.CurrentPhase, context.CurrentIteration, plan.ContinuePhase, plan.AdvancePhase, plan.NextCommands.Count);

            // Collect extracted findings
            phaseFindings.AddRange(plan.ExtractedFindings);

            // If AI says we're done with this phase
            if (!plan.ContinuePhase || plan.AdvancePhase)
            {
                _logger.LogInformation("Phase {Phase} complete: {Reasoning}", context.CurrentPhase, plan.Reasoning);
                return new PhaseResult
                {
                    Findings = phaseFindings,
                    CommandResults = phaseCommands,
                    ShouldAdvance = plan.AdvancePhase
                };
            }

            // Execute planned commands
            foreach (var commandRequest in plan.NextCommands)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var result = await _executeAgent.ExecuteAsync(commandRequest, ct);
                    phaseCommands.Add(result);

                    // Update context with latest results
                    context.PreviousResults = phaseCommands.TakeLast(5).ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Command execution failed: {Command}", commandRequest.Command);
                }
            }

            // Safety check: if no new commands and no progress, exit
            if (plan.NextCommands.Count == 0 && plan.ExtractedFindings.Count == 0)
            {
                _logger.LogWarning("Phase {Phase} made no progress. Advancing.", context.CurrentPhase);
                return new PhaseResult
                {
                    Findings = phaseFindings,
                    CommandResults = phaseCommands,
                    ShouldAdvance = true
                };
            }
        }

        // Max iterations reached
        _logger.LogWarning("Phase {Phase} reached max iterations ({Max})", 
            context.CurrentPhase, context.MaxIterations);

        return new PhaseResult
        {
            Findings = phaseFindings,
            CommandResults = phaseCommands,
            ShouldAdvance = true
        };
    }

    private Dictionary<string, object> GetPhaseGoals(AssessmentPhase phase, AssessmentRun run)
    {
        return phase switch
        {
            AssessmentPhase.Recon => new Dictionary<string, object>
            {
                ["objective"] = "Discover all live hosts, open ports, and services in target scope",
                ["minHosts"] = 1,
                ["requiredData"] = new[] { "open-ports", "service-versions" }
            },
            
            AssessmentPhase.Enumeration => new Dictionary<string, object>
            {
                ["objective"] = "Enumerate service-specific details, technologies, and potential entry points",
                ["requiredData"] = new[] { "web-tech-stack", "auth-mechanisms", "api-endpoints" }
            },
            
            AssessmentPhase.Prioritization => new Dictionary<string, object>
            {
                ["objective"] = "Correlate findings with CVE database and assess risk severity",
                ["minHighSeverity"] = 1
            },
            
            AssessmentPhase.Validation => new Dictionary<string, object>
            {
                ["objective"] = "Safely validate high-severity findings using approved techniques",
                ["allowedMethods"] = new[] { "config-audit", "cve-correlation", "safe-credential-test" },
                ["forbiddenMethods"] = new[] { "exploit-payload", "code-execution", "data-exfiltration" }
            },
            
            AssessmentPhase.Reporting => new Dictionary<string, object>
            {
                ["objective"] = "Compile evidence, score risk, and generate structured report"
            },
            
            _ => new Dictionary<string, object>()
        };
    }

    private List<AssessmentPhase> GetPhaseSequence()
    {
        return new List<AssessmentPhase>
        {
            AssessmentPhase.Recon,
            AssessmentPhase.Enumeration,
            AssessmentPhase.Prioritization,
            AssessmentPhase.Validation,
            AssessmentPhase.Reporting
        };
    }
}

public class PhaseResult
{
    public List<Finding> Findings { get; set; } = new();
    public List<CommandExecutionResult> CommandResults { get; set; } = new();
    public bool ShouldAdvance { get; set; }
}

public class AssessmentRunResult
{
    public required string AssessmentRunId { get; set; }
    public required bool Success { get; set; }
    public List<Finding> Findings { get; set; } = new();
    public List<CommandExecutionResult> CommandResults { get; set; } = new();
    public AssessmentEvidenceBundle? EvidenceBundle { get; set; }
    public string? ErrorMessage { get; set; }
}
