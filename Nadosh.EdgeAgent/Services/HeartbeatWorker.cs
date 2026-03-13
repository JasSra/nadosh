using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nadosh.EdgeAgent.Services;

public class HeartbeatWorker : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AgentConfiguration _config;
    private readonly ILogger<HeartbeatWorker> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    public HeartbeatWorker(
        IHttpClientFactory httpClientFactory,
        AgentConfiguration config,
        ILogger<HeartbeatWorker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit before starting heartbeats to allow enrollment to complete
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        _logger.LogInformation("Heartbeat service started (interval: {Interval}s)", _interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendHeartbeatAsync(stoppingToken);
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Heartbeat error: {Message}", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("Heartbeat service stopped");
    }

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("Mothership");
        
        var heartbeat = new
        {
            agentId = _config.AgentId,
            status = "active",
            cpuUsage = GetCpuUsage(),
            memoryUsage = GetMemoryUsage(),
            uptime = GetUptime(),
            lastSeen = DateTimeOffset.UtcNow
        };

        var response = await client.PostAsJsonAsync($"/api/edge/agents/{_config.AgentId}/heartbeat", heartbeat, cancellationToken);
        
        if (response.IsSuccessStatusCode)
        {
            _logger.LogDebug("♥ Heartbeat sent");
        }
        else
        {
            _logger.LogWarning("Heartbeat failed: {Status}", response.StatusCode);
        }
    }

    private double GetCpuUsage()
    {
        // Simplified - in production, use performance counters or /proc/stat
        return 0.0;
    }

    private double GetMemoryUsage()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var usedMB = process.WorkingSet64 / 1024.0 / 1024.0;
        return Math.Round(usedMB, 2);
    }

    private string GetUptime()
    {
        var uptime = DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
        return $"{(int)uptime.TotalHours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
    }
}
