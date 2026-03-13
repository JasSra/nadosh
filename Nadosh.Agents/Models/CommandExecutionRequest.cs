namespace Nadosh.Agents.Models;

/// <summary>
/// Request to execute a pentest command with safety boundaries.
/// </summary>
public class CommandExecutionRequest
{
    public required string Command { get; set; }
    public List<string> Arguments { get; set; } = new();
    public required string TargetScope { get; set; }
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
    public int TimeoutSeconds { get; set; } = 300; // 5 minutes default
    public bool CaptureOutput { get; set; } = true;
    public bool ValidateScope { get; set; } = true;
    public string? AuthorizedTaskId { get; set; }
    public string? ApprovalReference { get; set; }
}

/// <summary>
/// Result of command execution with captured output and metadata.
/// </summary>
public class CommandExecutionResult
{
    public required bool Success { get; set; }
    public required int ExitCode { get; set; }
    public required string StandardOutput { get; set; }
    public required string StandardError { get; set; }
    public required TimeSpan ExecutionTime { get; set; }
    public required string ExecutedCommand { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public bool TimedOut { get; set; }
}
