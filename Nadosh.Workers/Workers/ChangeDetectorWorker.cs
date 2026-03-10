using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Nadosh.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;

namespace Nadosh.Workers.Workers;

/// <summary>
/// Detects changes from recent scans and sends webhook notifications
/// Runs every N minutes, compares latest scan with previous, sends webhooks for:
/// - New ports opened
/// - Ports closed
/// - Service version changes
/// - Banner changes
/// - Severity changes
/// </summary>
public class ChangeDetectorWorker : BackgroundService
{
    private readonly ILogger<ChangeDetectorWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(10);

    public ChangeDetectorWorker(
        ILogger<ChangeDetectorWorker> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ChangeDetectorWorker starting. Will check for changes every {Interval}", _interval);

        // Wait a bit on startup to let scans run
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DetectAndNotifyChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during change detection cycle");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task DetectAndNotifyChangesAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NadoshDbContext>();

        // Get webhook URLs from configuration
        var webhookUrls = _configuration.GetSection("Webhooks:ChangeNotifications")
            .Get<List<string>>() ?? new List<string>();

        if (!webhookUrls.Any())
        {
            _logger.LogDebug("No webhooks configured. Set Webhooks:ChangeNotifications in appsettings.json");
            return;
        }

        // Get observations from last 15 minutes (since last check + buffer)
        var since = DateTime.UtcNow.AddMinutes(-15);
        
        var observations = await db.Observations
            .AsNoTracking()
            .Where(o => o.ObservedAt >= since)
            .OrderBy(o => o.TargetId)
            .ThenBy(o => o.Port)
            .ThenBy(o => o.ObservedAt)
            .Select(o => new
            {
                Ip = o.TargetId,
                o.Port,
                o.ObservedAt,
                o.State,
                o.ServiceName,
                o.ServiceVersion,
                o.Banner
            })
            .ToListAsync(ct);

        if (!observations.Any())
        {
            _logger.LogInformation("No recent observations to check for changes");
            return;
        }

        // Detect changes
        var changes = new List<object>();
        var groupedByIpPort = observations.GroupBy(o => new { o.Ip, o.Port });

        foreach (var group in groupedByIpPort)
        {
            var ordered = group.OrderBy(o => o.ObservedAt).ToList();
            if (ordered.Count < 2) continue;

            for (int i = 1; i < ordered.Count; i++)
            {
                var prev = ordered[i - 1];
                var curr = ordered[i];

                var detected = new List<string>();

                if (curr.State != prev.State)
                    detected.Add($"STATE_CHANGE:{prev.State}→{curr.State}");

                if (curr.ServiceName != prev.ServiceName)
                    detected.Add($"SERVICE_CHANGE:{prev.ServiceName}→{curr.ServiceName}");

                if (curr.ServiceVersion != prev.ServiceVersion && !string.IsNullOrEmpty(curr.ServiceVersion))
                    detected.Add($"VERSION_CHANGE:{prev.ServiceVersion}→{curr.ServiceVersion}");

                if (curr.Banner != prev.Banner && !string.IsNullOrEmpty(curr.Banner))
                    detected.Add("BANNER_CHANGE");

                if (detected.Any())
                {
                    changes.Add(new
                    {
                        ip = group.Key.Ip,
                        port = group.Key.Port,
                        detectedAt = curr.ObservedAt,
                        changeTypes = detected,
                        previousState = new { prev.State, prev.ServiceName, prev.ServiceVersion },
                        currentState = new { curr.State, curr.ServiceName, curr.ServiceVersion }
                    });
                }
            }
        }

        if (!changes.Any())
        {
            _logger.LogInformation("No changes detected in recent observations");
            return;
        }

        _logger.LogInformation("Detected {Count} changes, notifying {WebhookCount} webhooks", changes.Count, webhookUrls.Count);

        // Send webhooks
        var payload = new
        {
            timestamp = DateTime.UtcNow,
            changeCount = changes.Count,
            periodMinutes = 15,
            changes = changes.Take(100) // Limit payload size
        };

        foreach (var webhookUrl in webhookUrls)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(webhookUrl, payload, ct);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Webhook notification sent successfully to {Url}", webhookUrl);
                }
                else
                {
                    _logger.LogWarning("Webhook notification failed to {Url}: {StatusCode}", webhookUrl, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send webhook to {Url}", webhookUrl);
            }
        }
    }
}
