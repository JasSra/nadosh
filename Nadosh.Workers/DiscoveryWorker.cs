using System.Diagnostics;
using System.Net.Sockets;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;
using Nadosh.Infrastructure.Data;
using Nadosh.Infrastructure.Scanning;
using Microsoft.EntityFrameworkCore;

namespace Nadosh.Workers;

/// <summary>
/// Tier 0: High-speed async parallel port discovery scanner.
/// Uses Task.WhenAll with configurable concurrency to probe many ports simultaneously.
/// Rate-limited per /24 subnet via the shared Redis rate limiter.
/// Only records open/closed/filtered — no banner grabbing.
/// Open ports are funnelled to Tier 1 (BannerGrabJob).
/// Also performs ARP resolution to collect MAC addresses for local network targets.
/// </summary>
public class DiscoveryWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DiscoveryWorker> _logger;
    private readonly IJobQueue<BannerGrabJob> _bannerQueue;
    private readonly IScanRateLimiter _rateLimiter;
    private readonly ArpScanner _arpScanner;

    // Configurable via environment variables
    private readonly int _maxConcurrentProbes;
    private readonly int _connectTimeoutMs;

    public DiscoveryWorker(
        IServiceProvider serviceProvider,
        ILogger<DiscoveryWorker> logger,
        IJobQueue<BannerGrabJob> bannerQueue,
        IScanRateLimiter rateLimiter,
        ArpScanner arpScanner)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _bannerQueue = bannerQueue;
        _rateLimiter = rateLimiter;
        _arpScanner = arpScanner;

        _maxConcurrentProbes = int.TryParse(
            Environment.GetEnvironmentVariable("NADOSH_DISCOVERY_CONCURRENCY"), out var c) ? c : 200;
        _connectTimeoutMs = int.TryParse(
            Environment.GetEnvironmentVariable("NADOSH_DISCOVERY_TIMEOUT_MS"), out var t) ? t : 2000;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DiscoveryWorker starting (concurrency={Concurrency}, timeout={Timeout}ms)...",
            _maxConcurrentProbes, _connectTimeoutMs);

        using var scope = _serviceProvider.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue<Stage1ScanJob>>();

        while (!stoppingToken.IsCancellationRequested)
        {
            var msg = await queue.DequeueAsync(TimeSpan.FromMinutes(2), stoppingToken);
            if (msg == null)
            {
                await Task.Delay(1000, stoppingToken);
                continue;
            }

            try
            {
                await ProcessJobAsync(msg.Payload, scope.ServiceProvider, stoppingToken);
                await queue.AcknowledgeAsync(msg, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing discovery job for {TargetIp}", msg.Payload.TargetIp);
                if (msg.AttemptCount >= 3)
                    await queue.DeadLetterAsync(msg, ex.Message, stoppingToken);
                else
                    await queue.RejectAsync(msg, reenqueue: true, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(Stage1ScanJob job, IServiceProvider scopedProvider, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Discovery scan: {TargetIp} — {PortCount} ports",
            job.TargetIp, job.PortsToScan.Count);

        // Use SemaphoreSlim to control max concurrent probes
        using var semaphore = new SemaphoreSlim(_maxConcurrentProbes);
        var tasks = new List<Task<Observation>>();

        foreach (var port in job.PortsToScan)
        {
            await semaphore.WaitAsync(ct);
            tasks.Add(ProbeWithSemaphore(semaphore, job.TargetIp, port, job.BatchId, ct));
        }

        var observations = await Task.WhenAll(tasks);
        sw.Stop();

        // Persist all observations in batch
        var db = scopedProvider.GetRequiredService<NadoshDbContext>();
        db.Observations.AddRange(observations);
        await db.SaveChangesAsync(ct);

        // Attempt ARP resolution for local network targets (fire and forget, non-blocking)
        if (IsLocalNetwork(job.TargetIp))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _arpScanner.ResolveMacAddressAsync(job.TargetIp, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "ARP resolution failed for {Ip}", job.TargetIp);
                }
            }, ct);
        }

        // Funnel open ports to Tier 1 banner grab queue
        var openObs = observations.Where(o => o.State == "open").ToList();
        foreach (var obs in openObs)
        {
            await _bannerQueue.EnqueueAsync(new BannerGrabJob
            {
                BatchId = job.BatchId,
                TargetIp = job.TargetIp,
                Port = obs.Port,
                Protocol = obs.Protocol,
                DiscoveryObservationId = obs.Id
            }, idempotencyKey: $"banner:{job.TargetIp}:{obs.Port}:{job.BatchId}");
        }

        _logger.LogInformation(
            "Discovery complete: {TargetIp} — {Open} open, {Closed} closed, {Filtered} filtered in {ElapsedMs}ms",
            job.TargetIp,
            observations.Count(o => o.State == "open"),
            observations.Count(o => o.State == "closed"),
            observations.Count(o => o.State == "filtered"),
            sw.ElapsedMilliseconds);
    }

    private async Task<Observation> ProbeWithSemaphore(
        SemaphoreSlim semaphore, string ip, int port, string batchId, CancellationToken ct)
    {
        try
        {
            // Rate limit check
            var allowed = await _rateLimiter.TryAcquireAsync(ip, ct);
            if (!allowed)
            {
                // Back off briefly if rate limited
                await Task.Delay(100, ct);
                allowed = await _rateLimiter.TryAcquireAsync(ip, ct);
            }

            return await ProbePortAsync(ip, port, batchId, ct);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<Observation> ProbePortAsync(string ip, int port, string batchId, CancellationToken ct)
    {
        var obs = new Observation
        {
            TargetId = ip,
            ObservedAt = DateTime.UtcNow,
            Port = port,
            Protocol = "tcp",
            State = "filtered",
            ScanRunId = batchId,
            Tier = ScanTier.Discovery
        };

        var sw = Stopwatch.StartNew();

        try
        {
            using var tcpClient = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_connectTimeoutMs);

            await tcpClient.ConnectAsync(ip, port, timeoutCts.Token);

            sw.Stop();
            obs.LatencyMs = (int)sw.ElapsedMilliseconds;

            if (tcpClient.Connected)
            {
                obs.State = "open";
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            obs.State = "filtered"; // Timeout = filtered
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            obs.State = "closed"; // RST = closed (host alive, port closed)
        }
        catch (SocketException)
        {
            obs.State = "filtered"; // Other socket errors = filtered
        }

        sw.Stop();
        obs.LatencyMs ??= (int)sw.ElapsedMilliseconds;
        return obs;
    }

    private static bool IsLocalNetwork(string ip)
    {
        // Check if IP is in RFC1918 private ranges or link-local
        // 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 169.254.0.0/16
        return ip.StartsWith("10.") ||
               ip.StartsWith("192.168.") ||
               ip.StartsWith("169.254.") ||
               (ip.StartsWith("172.") && int.TryParse(ip.Split('.')[1], out var second) && second >= 16 && second <= 31);
    }
}
