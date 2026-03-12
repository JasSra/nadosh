using System.Diagnostics;
using System.Net.Sockets;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;
using Nadosh.Infrastructure.Data;
using Nadosh.Infrastructure.Scanning;
using Microsoft.EntityFrameworkCore;
using Nadosh.Workers.Queue;

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
    private static readonly string WorkerId = $"{Environment.MachineName}/discovery/{Environment.ProcessId}";
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
        var queuePolicy = scope.ServiceProvider.GetRequiredService<IQueuePolicyProvider>().GetPolicy<Stage1ScanJob>();

        while (!stoppingToken.IsCancellationRequested)
        {
            var msg = await queue.DequeueAsync(queuePolicy.VisibilityTimeout, stoppingToken);
            if (msg == null)
            {
                await Task.Delay(1000, stoppingToken);
                continue;
            }

            try
            {
                await QueueProcessingUtilities.RunWithLeaseHeartbeatAsync(
                    queue,
                    msg,
                    queuePolicy,
                    ct => ProcessJobAsync(msg.Payload, scope.ServiceProvider, ct),
                    _logger,
                    stoppingToken);
                await queue.AcknowledgeAsync(msg, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing discovery job for {TargetIp}", msg.Payload.TargetIp);
                await QueueProcessingUtilities.RejectWithBackoffOrDeadLetterAsync(queue, msg, ex, _logger, queuePolicy, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(Stage1ScanJob job, IServiceProvider scopedProvider, CancellationToken ct)
    {
        var dispatchStateService = scopedProvider.GetRequiredService<IStage1DispatchStateService>();
        var handoffDispatchService = scopedProvider.GetRequiredService<IObservationHandoffDispatchService>();
        var startResult = await dispatchStateService.StartAsync(
            job.BatchId,
            job.TargetIp,
            job.PortsToScan,
            WorkerId,
            ct);

        if (startResult.Status == Stage1DispatchTransitionStatus.Rejected)
        {
            _logger.LogInformation(
                "Skipping discovery job for {TargetIp} in batch {BatchId}: {Reason}",
                job.TargetIp,
                job.BatchId,
                startResult.Reason);
            return;
        }

        if (startResult.Status == Stage1DispatchTransitionStatus.NoOp)
        {
            _logger.LogInformation(
                "Skipping duplicate discovery job for {TargetIp} in batch {BatchId}: {Reason}",
                job.TargetIp,
                job.BatchId,
                startResult.Reason);
            return;
        }

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

        try
        {
            var observations = await Task.WhenAll(tasks);
            sw.Stop();

            // Persist all observations in batch
            var db = scopedProvider.GetRequiredService<NadoshDbContext>();
            db.Observations.AddRange(observations);
            await db.SaveChangesAsync(ct);

            await dispatchStateService.CompleteAsync(
                job.BatchId,
                job.TargetIp,
                WorkerId,
                observations,
                ct);

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

            // SNMP enumeration for port 161 (UDP) if discovered
            var snmpObs = observations.FirstOrDefault(o => o.Port == 161 && o.Protocol == "UDP" && o.State == "open");
            if (snmpObs != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var snmpService = scopedProvider.GetRequiredService<Nadosh.Core.Services.SnmpScannerService>();
                        var result = await snmpService.ScanAsync(job.TargetIp, 161);
                        
                        if (result != null && result.IsAccessible && result.DeviceInfo != null)
                        {
                            using var snmpScope = _serviceProvider.CreateScope();
                            var snmpDb = snmpScope.ServiceProvider.GetRequiredService<NadoshDbContext>();
                            
                            var deviceInfo = result.DeviceInfo;
                            
                            // Update existing observation with SNMP device info
                            snmpObs.ServiceName = "snmp";
                            snmpObs.ServiceVersion = result.CommunityString; // Store working community string
                            snmpObs.DeviceType = ExtractDeviceType(deviceInfo.SysDescr);
                            snmpDb.Observations.Update(snmpObs);
                            
                            // Store as enrichment result
                            var enrichment = new EnrichmentResult
                            {
                                ObservationId = snmpObs.Id,
                                RuleId = "snmp-discovery",
                                RuleVersion = "1.0",
                                ResultStatus = "success",
                                Confidence = 1.0,
                                Tags = new[] { "snmp", "device-enumeration" },
                                Summary = $"SNMP Device: {deviceInfo.SysName ?? "Unknown"} - {deviceInfo.SysDescr?.Substring(0, Math.Min(100, deviceInfo.SysDescr.Length)) ?? "No description"}",
                                EvidenceJson = System.Text.Json.JsonSerializer.Serialize(new
                                {
                                    deviceInfo.SysDescr,
                                    deviceInfo.SysName,
                                    deviceInfo.SysUpTime,
                                    deviceInfo.SysLocation,
                                    deviceInfo.SysContact,
                                    CommunityString = result.CommunityString
                                }),
                                ExecutedAt = DateTime.UtcNow
                            };
                            
                            snmpDb.EnrichmentResults.Add(enrichment);
                            await snmpDb.SaveChangesAsync(ct);
                            
                            _logger.LogInformation(
                                "SNMP enumeration successful for {TargetIp}: {SysName} - {SysDescr}",
                                job.TargetIp,
                                deviceInfo.SysName ?? "Unknown",
                                deviceInfo.SysDescr?.Substring(0, Math.Min(50, deviceInfo.SysDescr.Length)) ?? "N/A");
                        }
                        else
                        {
                            _logger.LogDebug("SNMP enumeration failed for {TargetIp} (no response or access denied)", job.TargetIp);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "SNMP enumeration failed for {Ip}", job.TargetIp);
                    }
                }, ct);
            }

            // Funnel open ports to Tier 1 banner grab queue
            var openObs = observations.Where(o => o.State == "open").ToList();
            foreach (var obs in openObs)
            {
                var handoffScheduleResult = await handoffDispatchService.ScheduleAsync(
                    ObservationHandoffDispatchKind.BannerGrab,
                    obs.Id,
                    job.BatchId,
                    job.TargetIp,
                    obs.Port,
                    obs.Protocol,
                    obs.ServiceName,
                    WorkerId,
                    ct);

                if (handoffScheduleResult.Status == ObservationHandoffDispatchTransitionStatus.Rejected)
                {
                    _logger.LogWarning(
                        "Skipping banner-grab enqueue for observation {ObservationId}: {Reason}",
                        obs.Id,
                        handoffScheduleResult.Reason);
                    continue;
                }

                try
                {
                    await _bannerQueue.EnqueueAsync(new BannerGrabJob
                    {
                        BatchId = job.BatchId,
                        TargetIp = job.TargetIp,
                        Port = obs.Port,
                        Protocol = obs.Protocol,
                        DiscoveryObservationId = obs.Id
                    },
                    idempotencyKey: $"banner:{job.TargetIp}:{obs.Port}:{job.BatchId}",
                    priority: 0,
                    enqueueOptions: new JobEnqueueOptions { ShardKey = job.TargetIp },
                    cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    await handoffDispatchService.FailAsync(
                        ObservationHandoffDispatchKind.BannerGrab,
                        obs.Id,
                        WorkerId,
                        ex.Message,
                        cancellationToken: ct);

                    _logger.LogError(
                        ex,
                        "Failed to enqueue banner-grab handoff for {TargetIp}:{Port} (observation {ObservationId})",
                        job.TargetIp,
                        obs.Port,
                        obs.Id);
                }
            }

            _logger.LogInformation(
                "Discovery complete: {TargetIp} — {Open} open, {Closed} closed, {Filtered} filtered in {ElapsedMs}ms",
                job.TargetIp,
                observations.Count(o => o.State == "open"),
                observations.Count(o => o.State == "closed"),
                observations.Count(o => o.State == "filtered"),
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            await dispatchStateService.FailAsync(job.BatchId, job.TargetIp, WorkerId, ex.Message, ct);
            throw;
        }
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

    private static string ExtractDeviceType(string? sysDescr)
    {
        if (string.IsNullOrEmpty(sysDescr))
            return "unknown";

        var lower = sysDescr.ToLowerInvariant();
        
        if (lower.Contains("router") || lower.Contains("cisco") && lower.Contains("ios"))
            return "router";
        if (lower.Contains("switch") || lower.Contains("catalyst"))
            return "switch";
        if (lower.Contains("firewall") || lower.Contains("fortigate") || lower.Contains("palo alto"))
            return "firewall";
        if (lower.Contains("printer") || lower.Contains("hp ") || lower.Contains("lexmark"))
            return "printer";
        if (lower.Contains("camera") || lower.Contains("ip cam") || lower.Contains("axis"))
            return "camera";
        if (lower.Contains("nas") || lower.Contains("storage") || lower.Contains("synology") || lower.Contains("qnap"))
            return "nas";
        if (lower.Contains("linux") || lower.Contains("ubuntu") || lower.Contains("debian") || lower.Contains("redhat"))
            return "linux-server";
        if (lower.Contains("windows") || lower.Contains("microsoft"))
            return "windows-server";
        if (lower.Contains("access point") || lower.Contains("wireless") || lower.Contains("ubiquiti"))
            return "access-point";
        if (lower.Contains("phone") || lower.Contains("voip") || lower.Contains("pbx"))
            return "voip-phone";
        
        return "network-device";
    }
}
