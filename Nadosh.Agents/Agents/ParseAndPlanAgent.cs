using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Nadosh.Agents.Models;
using System.Text;
using System.Text.Json;

namespace Nadosh.Agents.Agents;

/// <summary>
/// AI-powered agent that parses tool output, extracts findings, and plans next assessment steps.
/// Uses Semantic Kernel with structured prompting for intelligent decision-making.
/// </summary>
public class ParseAndPlanAgent
{
    private readonly ILogger<ParseAndPlanAgent> _logger;
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatCompletion;

    public ParseAndPlanAgent(
        ILogger<ParseAndPlanAgent> logger,
        Kernel kernel)
    {
        _logger = logger;
        _kernel = kernel;
        _chatCompletion = kernel.GetRequiredService<IChatCompletionService>();
    }

    /// <summary>
    /// Parses previous command results and plans next assessment actions.
    /// </summary>
    public async Task<AgentPlanResult> ParseAndPlanAsync(
        AgentPlanningContext context,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Planning assessment phase: {Phase}, iteration {Iteration}/{Max}",
            context.CurrentPhase, context.CurrentIteration, context.MaxIterations);

        try
        {
            // Build prompt with context
            var prompt = BuildPlanningPrompt(context);

            // Get AI response
            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(GetSystemPrompt(context.CurrentPhase));
            chatHistory.AddUserMessage(prompt);

            var response = await _chatCompletion.GetChatMessageContentAsync(
                chatHistory,
                kernel: _kernel,
                cancellationToken: ct);

            // Parse AI response into structured plan
            var plan = ParseAiResponse(response.Content ?? string.Empty, context);

            _logger.LogInformation("Plan generated: continue={Continue}, advance={Advance}, commands={CommandCount}",
                plan.ContinuePhase, plan.AdvancePhase, plan.NextCommands.Count);

            return plan;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate assessment plan");
            
            // Return safe fallback plan
            return new AgentPlanResult
            {
                ContinuePhase = false,
                AdvancePhase = false,
                Reasoning = $"Error during planning: {ex.Message}"
            };
        }
    }

    private string GetSystemPrompt(AssessmentPhase phase)
    {
        return phase switch
        {
            AssessmentPhase.Recon => @"You are a penetration testing assistant specializing in reconnaissance.
Your role is to analyze network scan results and identify interesting targets for deeper enumeration.

AVAILABLE TOOL CATEGORIES:
- Discovery: DNS enumeration, subdomain discovery, whois, host probing
- NetworkScanning: nmap, masscan, port scanning, service detection
- Forensics: tcpdump, tshark, packet analysis

Focus on: open ports, service versions, OS fingerprints, and potential attack surface.
Choose tools from appropriate categories based on the current objective.
Extract findings as structured JSON and recommend next enumeration steps.
Always stay within authorized scope and avoid destructive actions.",

            AssessmentPhase.Enumeration => @"You are a penetration testing assistant specializing in service enumeration.
Your role is to analyze service-specific scans and identify vulnerabilities or misconfigurations.

AVAILABLE TOOL CATEGORIES:
- WebScanning: nikto, gobuster, ffuf, dirb, whatweb, wpscan, wafw00f
- Enumeration: enum4linux, smbclient, smbmap, rpcclient, ldapsearch, snmpwalk
- Vulnerability: nuclei, sqlmap, sslscan, testssl.sh, commix, xsser

Focus on: web technologies, authentication mechanisms, exposed APIs, SMB/RPC services, and service-specific flaws.
Select tools from relevant categories for the discovered services.
Extract findings as structured JSON and recommend validation tests for confirmed issues.",

            AssessmentPhase.Validation => @"You are a penetration testing assistant specializing in vulnerability validation.
Your role is to confirm potential vulnerabilities using safe, non-destructive validation techniques.

AVAILABLE TOOL CATEGORIES:
- Vulnerability: CVE scanners, configuration auditors, safe validation tools
- Enumeration: Deep service probing, credential enumeration (authorized only)
- Discovery: Additional reconnaissance to confirm findings

Focus on: CVE correlation, configuration audits, credential testing (with authorization), patch verification.
Use searchsploit for CVE lookups, nuclei for template-based validation.
NEVER recommend exploit payloads or destructive actions. Only safe validation.",

            AssessmentPhase.Reporting => @"You are a penetration testing assistant specializing in evidence synthesis.
Your role is to compile findings, assess risk, and prepare structured reports.

TOOL USAGE:
- Forensics: binwalk, foremost, volatility for artifact analysis
- Utility: jq for JSON processing, git for evidence versioning

Focus on: severity scoring, business impact, remediation guidance, and evidence linkage.
Categorize findings by severity and affected service categories.",

            _ => @"You are a penetration testing assistant. Analyze results and provide safe, structured recommendations.

Choose tools from these categories based on objectives:
- Discovery, NetworkScanning, WebScanning, Enumeration, Vulnerability, PasswordAttack, Exploitation, Forensics, Utility"
        };
    }

    private string BuildPlanningPrompt(AgentPlanningContext context)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# Assessment Context");
        sb.AppendLine($"Run ID: {context.AssessmentRunId}");
        sb.AppendLine($"Phase: {context.CurrentPhase}");
        sb.AppendLine($"Target Scope: {context.TargetScope}");
        sb.AppendLine($"Iteration: {context.CurrentIteration}/{context.MaxIterations}");
        sb.AppendLine();

        sb.AppendLine($"# Phase Goals");
        foreach (var (key, value) in context.PhaseGoals)
        {
            sb.AppendLine($"- {key}: {value}");
        }
        sb.AppendLine();

        var suites = context.AvailableTools
            .GroupBy(tool => string.IsNullOrWhiteSpace(tool.ToolSuite) ? "uncategorized" : tool.ToolSuite)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        sb.AppendLine($"# Available Tools ({context.AvailableTools.Count} across {suites.Count} suites)");
        foreach (var suite in suites.Take(8))
        {
            var toolNames = string.Join(", ", suite
                .OrderBy(tool => tool.ToolName, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .Select(tool => tool.ToolName));

            sb.AppendLine($"- Suite {suite.Key}: {toolNames}");
        }
        sb.AppendLine();

        foreach (var tool in context.AvailableTools.Take(12)) // Limit for token efficiency
        {
            var suiteLabel = string.IsNullOrWhiteSpace(tool.ToolSuite) ? "uncategorized" : tool.ToolSuite;
            sb.AppendLine($"- {tool.ToolName} [{suiteLabel}/{tool.Category}]: {tool.UsageHint}");
        }
        sb.AppendLine();

        if (context.PreviousResults.Any())
        {
            sb.AppendLine($"# Previous Command Results ({context.PreviousResults.Count})");
            foreach (var result in context.PreviousResults.TakeLast(3)) // Last 3 for context
            {
                sb.AppendLine($"## {result.ExecutedCommand}");
                sb.AppendLine($"Exit Code: {result.ExitCode}, Duration: {result.ExecutionTime.TotalSeconds:F1}s");
                
                var output = result.StandardOutput.Length > 500 
                    ? result.StandardOutput[..500] + "... (truncated)"
                    : result.StandardOutput;
                sb.AppendLine($"Output: {output}");
                sb.AppendLine();
            }
        }

        if (context.CurrentFindings.Any())
        {
            sb.AppendLine($"# Current Findings ({context.CurrentFindings.Count})");
            foreach (var finding in context.CurrentFindings)
            {
                sb.AppendLine($"- [{finding.Severity}] {finding.Type}: {finding.Description}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("# Your Task");
        sb.AppendLine("Analyze the results above and respond with a JSON object containing:");
        sb.AppendLine("1. extractedFindings: Array of new findings from command output (type, severity, description, target, metadata)");
        sb.AppendLine("2. continuePhase: boolean - should we continue this phase?");
        sb.AppendLine("3. advancePhase: boolean - ready to advance to next phase?");
        sb.AppendLine("4. nextPhase: string - which phase to advance to (if advancing)");
        sb.AppendLine("5. nextCommands: Array of commands to execute next (command, arguments[], targetScope)");
        sb.AppendLine("6. reasoning: string - explain your decision");
        sb.AppendLine("7. newObjectives: Array of strings - new objectives identified");
        sb.AppendLine();
        sb.AppendLine("Respond ONLY with valid JSON, no markdown formatting.");

        return sb.ToString();
    }

    private AgentPlanResult ParseAiResponse(string aiResponse, AgentPlanningContext context)
    {
        try
        {
            // Clean markdown code blocks if present
            var jsonContent = aiResponse.Trim();
            if (jsonContent.StartsWith("```json"))
            {
                jsonContent = jsonContent[7..];
            }
            if (jsonContent.StartsWith("```"))
            {
                jsonContent = jsonContent[3..];
            }
            if (jsonContent.EndsWith("```"))
            {
                jsonContent = jsonContent[..^3];
            }
            jsonContent = jsonContent.Trim();

            // Parse JSON response
            var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            var plan = new AgentPlanResult
            {
                ContinuePhase = root.TryGetProperty("continuePhase", out var cont) && cont.GetBoolean(),
                AdvancePhase = root.TryGetProperty("advancePhase", out var adv) && adv.GetBoolean(),
                Reasoning = root.TryGetProperty("reasoning", out var reason) ? reason.GetString() : null
            };

            // Parse next phase
            if (root.TryGetProperty("nextPhase", out var nextPhase) && nextPhase.ValueKind == JsonValueKind.String)
            {
                if (Enum.TryParse<AssessmentPhase>(nextPhase.GetString(), true, out var phase))
                {
                    plan.NextPhase = phase;
                }
            }

            // Parse extracted findings
            if (root.TryGetProperty("extractedFindings", out var findings) && findings.ValueKind == JsonValueKind.Array)
            {
                foreach (var findingElement in findings.EnumerateArray())
                {
                    try
                    {
                        var finding = new Finding
                        {
                            Type = findingElement.GetProperty("type").GetString() ?? "unknown",
                            Severity = findingElement.GetProperty("severity").GetString() ?? "info",
                            Description = findingElement.GetProperty("description").GetString() ?? string.Empty,
                            Target = findingElement.GetProperty("target").GetString() ?? string.Empty
                        };

                        if (findingElement.TryGetProperty("metadata", out var metadata))
                        {
                            foreach (var prop in metadata.EnumerateObject())
                            {
                                finding.Metadata[prop.Name] = prop.Value.ToString();
                            }
                        }

                        plan.ExtractedFindings.Add(finding);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse finding from AI response");
                    }
                }
            }

            // Parse next commands
            if (root.TryGetProperty("nextCommands", out var commands) && commands.ValueKind == JsonValueKind.Array)
            {
                foreach (var cmdElement in commands.EnumerateArray())
                {
                    try
                    {
                        var cmd = new CommandExecutionRequest
                        {
                            Command = cmdElement.GetProperty("command").GetString() ?? string.Empty,
                            TargetScope = cmdElement.TryGetProperty("targetScope", out var scope) 
                                ? scope.GetString() ?? context.TargetScope 
                                : context.TargetScope
                        };

                        if (cmdElement.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Array)
                        {
                            cmd.Arguments = args.EnumerateArray()
                                .Select(a => a.GetString() ?? string.Empty)
                                .ToList();
                        }

                        plan.NextCommands.Add(cmd);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse command from AI response");
                    }
                }
            }

            // Parse new objectives
            if (root.TryGetProperty("newObjectives", out var objectives) && objectives.ValueKind == JsonValueKind.Array)
            {
                plan.NewObjectives = objectives.EnumerateArray()
                    .Select(o => o.GetString() ?? string.Empty)
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .ToList();
            }

            return plan;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse AI response as JSON: {Response}", aiResponse);
            
            // Fallback: return plan with reasoning from raw response
            return new AgentPlanResult
            {
                ContinuePhase = context.CurrentIteration < context.MaxIterations,
                AdvancePhase = false,
                Reasoning = $"Failed to parse structured response. Raw AI output: {aiResponse[..Math.Min(200, aiResponse.Length)]}"
            };
        }
    }
}
