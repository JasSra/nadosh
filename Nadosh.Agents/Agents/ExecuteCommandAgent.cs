using Microsoft.Extensions.Logging;
using Nadosh.Agents.Models;
using Nadosh.Core.Interfaces;
using System.Diagnostics;
using System.Text;

namespace Nadosh.Agents.Agents;

/// <summary>
/// Agent that executes pentest commands with safety boundaries and scope validation.
/// Captures output, enforces timeouts, and validates targets against authorized scopes.
/// </summary>
public class ExecuteCommandAgent
{
    private readonly ILogger<ExecuteCommandAgent> _logger;
    private readonly IAssessmentPolicyService _policyService;

    public ExecuteCommandAgent(
        ILogger<ExecuteCommandAgent> logger,
        IAssessmentPolicyService policyService)
    {
        _logger = logger;
        _policyService = policyService;
    }

    /// <summary>
    /// Executes a command with full safety checks and output capture.
    /// </summary>
    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandExecutionRequest request,
        CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Validate scope if requested
            if (request.ValidateScope)
            {
                var scopeValid = await ValidateScopeAsync(request, ct);
                if (!scopeValid)
                {
                    return new CommandExecutionResult
                    {
                        Success = false,
                        ExitCode = -1,
                        StandardOutput = string.Empty,
                        StandardError = "Scope validation failed: target not in authorized scope",
                        ExecutionTime = stopwatch.Elapsed,
                        ExecutedCommand = $"{request.Command} {string.Join(" ", request.Arguments)}",
                        StartedAt = startTime,
                        CompletedAt = DateTime.UtcNow,
                        ErrorMessage = "Target scope not authorized"
                    };
                }
            }

            // Sanitize command and arguments
            var sanitizedCommand = SanitizeCommand(request.Command);
            var sanitizedArgs = request.Arguments.Select(SanitizeArgument).ToList();

            _logger.LogInformation("Executing command: {Command} {Args} (timeout: {Timeout}s)",
                sanitizedCommand, string.Join(" ", sanitizedArgs), request.TimeoutSeconds);

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = sanitizedCommand,
                    Arguments = string.Join(" ", sanitizedArgs),
                    WorkingDirectory = request.WorkingDirectory ?? "/tmp",
                    RedirectStandardOutput = request.CaptureOutput,
                    RedirectStandardError = request.CaptureOutput,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            // Add environment variables
            foreach (var (key, value) in request.EnvironmentVariables)
            {
                process.StartInfo.EnvironmentVariables[key] = value;
            }

            // Capture output streams
            if (request.CaptureOutput)
            {
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                };
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        errorBuilder.AppendLine(e.Data);
                    }
                };
            }

            process.Start();

            if (request.CaptureOutput)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            // Wait with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Command timed out after {Timeout}s: {Command}",
                    request.TimeoutSeconds, sanitizedCommand);
                
                try { process.Kill(entireProcessTree: true); } catch { /* Best effort */ }

                return new CommandExecutionResult
                {
                    Success = false,
                    ExitCode = -2,
                    StandardOutput = outputBuilder.ToString(),
                    StandardError = errorBuilder.ToString(),
                    ExecutionTime = stopwatch.Elapsed,
                    ExecutedCommand = $"{sanitizedCommand} {string.Join(" ", sanitizedArgs)}",
                    StartedAt = startTime,
                    CompletedAt = DateTime.UtcNow,
                    ErrorMessage = "Command execution timed out",
                    TimedOut = true
                };
            }

            stopwatch.Stop();

            var result = new CommandExecutionResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                StandardOutput = outputBuilder.ToString(),
                StandardError = errorBuilder.ToString(),
                ExecutionTime = stopwatch.Elapsed,
                ExecutedCommand = $"{sanitizedCommand} {string.Join(" ", sanitizedArgs)}",
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow,
                TimedOut = false
            };

            _logger.LogInformation("Command completed: exit={ExitCode}, duration={Duration}ms",
                result.ExitCode, result.ExecutionTime.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command execution failed: {Command}", request.Command);
            
            return new CommandExecutionResult
            {
                Success = false,
                ExitCode = -3,
                StandardOutput = string.Empty,
                StandardError = ex.Message,
                ExecutionTime = stopwatch.Elapsed,
                ExecutedCommand = $"{request.Command} {string.Join(" ", request.Arguments)}",
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<bool> ValidateScopeAsync(CommandExecutionRequest request, CancellationToken ct)
    {
        try
        {
            // Extract target IP/hostname from arguments or target scope
            var targets = ExtractTargetsFromRequest(request);
            
            // For each target, validate against policy
            foreach (var target in targets)
            {
                // This is a simplified check - in production, integrate with full policy service
                if (string.IsNullOrWhiteSpace(target))
                    continue;

                // Check if target matches authorized scope pattern
                if (!IsTargetInScope(target, request.TargetScope))
                {
                    _logger.LogWarning("Target {Target} not in authorized scope {Scope}",
                        target, request.TargetScope);
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scope validation failed");
            return false;
        }
    }

    private List<string> ExtractTargetsFromRequest(CommandExecutionRequest request)
    {
        var targets = new List<string>();

        // Look for target patterns in arguments (-h, --host, -u, --url, etc.)
        for (int i = 0; i < request.Arguments.Count; i++)
        {
            var arg = request.Arguments[i];
            
            if ((arg == "-h" || arg == "--host" || arg == "-u" || arg == "--url" || arg == "-t" || arg == "--target")
                && i + 1 < request.Arguments.Count)
            {
                targets.Add(request.Arguments[i + 1]);
            }
            // Positional argument might be target
            else if (!arg.StartsWith("-") && Uri.TryCreate(arg, UriKind.Absolute, out _))
            {
                targets.Add(arg);
            }
        }

        return targets;
    }

    private bool IsTargetInScope(string target, string authorizedScope)
    {
        // Simplified scope check - production should use CIDR matching, DNS resolution, etc.
        // For now, check if target matches scope pattern or is within CIDR
        
        if (authorizedScope == "*" || authorizedScope == "any")
            return false; // Reject wildcard scopes

        if (target.Contains(authorizedScope, StringComparison.OrdinalIgnoreCase))
            return true;

        // TODO: Add proper CIDR matching, DNS resolution, scope tag validation
        return false;
    }

    private string SanitizeCommand(string command)
    {
        // Remove shell metacharacters and path traversal
        var sanitized = command.Trim();
        
        // Reject commands with shell metacharacters
        if (sanitized.Contains(';') || sanitized.Contains('|') || sanitized.Contains('&') || 
            sanitized.Contains('`') || sanitized.Contains('$'))
        {
            throw new ArgumentException("Command contains invalid shell metacharacters");
        }

        return sanitized;
    }

    private string SanitizeArgument(string arg)
    {
        // Basic argument sanitization
        return arg.Trim();
    }
}
