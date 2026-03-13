using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nadosh.EdgeAgent.Services;

public class TaskExecutionWorker : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AgentConfiguration _config;
    private readonly ILogger<TaskExecutionWorker> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(10);

    public TaskExecutionWorker(
        IHttpClientFactory httpClientFactory,
        AgentConfiguration config,
        ILogger<TaskExecutionWorker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit before starting task polling
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        _logger.LogInformation("Task execution worker started (poll interval: {Interval}s)", _pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndExecuteTasksAsync(stoppingToken);
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Task execution error: {Message}", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }

        _logger.LogInformation("Task execution worker stopped");
    }

    private async Task PollAndExecuteTasksAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("Mothership");
        
        // Poll for pending tasks
        var response = await client.GetAsync($"/api/edge/agents/{_config.AgentId}/tasks?status=pending&limit=1", cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var tasks = JsonSerializer.Deserialize<JsonElement>(content);
        if (tasks.ValueKind != JsonValueKind.Array || tasks.GetArrayLength() == 0)
        {
            return;
        }

        // Execute first task
        var task = tasks[0];
        var taskId = task.GetProperty("taskId").GetString();
        var taskType = task.GetProperty("taskType").GetString();
        var payload = task.TryGetProperty("payload", out var p) ? p : (JsonElement?)null;

        _logger.LogInformation("⚡ Received task: {TaskId} (type: {Type})", taskId, taskType);

        await ExecuteTaskAsync(taskId!, taskType!, payload, cancellationToken);
    }

    private async Task ExecuteTaskAsync(string taskId, string taskType, JsonElement? payload, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("Mothership");
        
        try
        {
            // Update task status to running
            await client.PatchAsJsonAsync($"/api/edge/tasks/{taskId}/status", new { status = "running" }, cancellationToken);

            string output;
            bool success;

            switch (taskType.ToLowerInvariant())
            {
                case "execute_command":
                    (success, output) = await ExecuteCommandAsync(payload, cancellationToken);
                    break;

                case "collect_system_info":
                    (success, output) = await CollectSystemInfoAsync();
                    break;

                case "scan_network":
                    (success, output) = await ScanNetworkAsync(payload);
                    break;

                case "update_agent":
                    (success, output) = await UpdateAgentAsync(payload);
                    break;

                default:
                    success = false;
                    output = $"Unknown task type: {taskType}";
                    break;
            }

            // Report result
            var result = new
            {
                status = success ? "completed" : "failed",
                output = output,
                executedAt = DateTimeOffset.UtcNow,
                executedBy = _config.AgentId
            };

            await client.PatchAsJsonAsync($"/api/edge/tasks/{taskId}/result", result, cancellationToken);

            _logger.LogInformation("✓ Task {TaskId} completed: {Status}", taskId, success ? "success" : "failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Task execution failed: {Message}", ex.Message);
            
            // Report failure
            await client.PatchAsJsonAsync($"/api/edge/tasks/{taskId}/result", new
            {
                status = "failed",
                output = $"Exception: {ex.Message}",
                executedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
        }
    }

    private async Task<(bool success, string output)> ExecuteCommandAsync(JsonElement? payload, CancellationToken cancellationToken)
    {
        if (payload == null || !payload.Value.TryGetProperty("command", out var cmdElement))
        {
            return (false, "Missing 'command' in payload");
        }

        var command = cmdElement.GetString()!;
        _logger.LogInformation("Executing command: {Command}", command);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "powershell" : "/bin/bash",
                Arguments = OperatingSystem.IsWindows() ? $"-Command \"{command}\"" : $"-c \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, "Failed to start process");
            }

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();
            var fullOutput = $"Exit Code: {process.ExitCode}\n\nOutput:\n{output}\n\nError:\n{error}";

            return (process.ExitCode == 0, fullOutput);
        }
        catch (Exception ex)
        {
            return (false, $"Exception: {ex.Message}");
        }
    }

    private Task<(bool success, string output)> CollectSystemInfoAsync()
    {
        var info = new StringBuilder();
        info.AppendLine($"Hostname: {Environment.MachineName}");
        info.AppendLine($"OS: {Environment.OSVersion}");
        info.AppendLine($"Platform: {_config.Platform}");
        info.AppendLine($"User: {Environment.UserName}");
        info.AppendLine($"Working Directory: {Environment.CurrentDirectory}");
        info.AppendLine($"Processors: {Environment.ProcessorCount}");
        info.AppendLine($".NET Version: {Environment.Version}");

        return Task.FromResult((true, info.ToString()));
    }

    private Task<(bool success, string output)> ScanNetworkAsync(JsonElement? payload)
    {
        // Placeholder - would integrate with nmap or similar
        return Task.FromResult((true, "Network scanning not yet implemented. Would use nmap/masscan."));
    }

    private Task<(bool success, string output)> UpdateAgentAsync(JsonElement? payload)
    {
        // Placeholder - would download and apply updates
        return Task.FromResult((true, "Agent update not yet implemented. Would download from GitHub releases."));
    }
}
