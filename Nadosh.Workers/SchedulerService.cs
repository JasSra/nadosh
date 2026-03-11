using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;
using Nadosh.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Nadosh.Workers;

/// <summary>
/// Adaptive Decay Scheduler with leader election.
/// - Only one instance runs at a time across all containers (Redis leader lock).
/// - Scores targets based on open ports, state changes, and interest level.
/// - Adjusts scan cadence: Cold→90d, Standard→15d, Warm→24h, Hot→6h, Critical→1h.
/// - Uses IPortSelectionStrategy to pick intelligent port lists per target.
/// </summary>
public class SchedulerService : BackgroundService
{
    private static readonly string WorkerId = $"{Environment.MachineName}/scheduler/{Environment.ProcessId}";
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SchedulerService> _logger;
    private readonly ILeaderElection _leaderElection;
    private readonly string _instanceId = $"scheduler-{Environment.MachineName}-{Guid.NewGuid().ToString("N")[..8]}";

    private static readonly TimeSpan LeaderTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ScheduleInterval = TimeSpan.FromSeconds(30);

    public SchedulerService(
        IServiceProvider serviceProvider,
        ILogger<SchedulerService> logger,
        ILeaderElection leaderElection)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _leaderElection = leaderElection;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SchedulerService starting (instance={Instance})...", _instanceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var isLeader = await _leaderElection.TryAcquireLeadershipAsync(
                    "nadosh:scheduler:leader", _instanceId, LeaderTtl, stoppingToken);

                if (!isLeader)
                {
                    _logger.LogDebug("Not the scheduler leader, sleeping...");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }

                _logger.LogInformation("Acquired scheduler leadership");

                // Main scheduling loop while we hold leadership
                while (!stoppingToken.IsCancellationRequested)
                {
                    var renewed = await _leaderElection.RenewLeadershipAsync(
                        "nadosh:scheduler:leader", _instanceId, LeaderTtl, stoppingToken);

                    if (!renewed)
                    {
                        _logger.LogWarning("Lost scheduler leadership");
                        break;
                    }

                    await ScheduleDueScansAsync(stoppingToken);
                    await Task.Delay(ScheduleInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in scheduler loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        // Release leadership on shutdown
        try
        {
            await _leaderElection.ReleaseLeadershipAsync(
                "nadosh:scheduler:leader", _instanceId, CancellationToken.None);
        }
        catch { /* best effort */ }
    }

    private async Task ScheduleDueScansAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NadoshDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue<Stage1ScanJob>>();
        var portStrategy = scope.ServiceProvider.GetRequiredService<IPortSelectionStrategy>();
        var dispatchStateService = scope.ServiceProvider.GetRequiredService<IStage1DispatchStateService>();

        var now = DateTime.UtcNow;

        // Fetch targets that are due for scanning
        var dueTargets = await db.Targets
            .Where(t => t.Monitored && (!t.NextScheduled.HasValue || t.NextScheduled <= now))
            .OrderBy(t => t.NextScheduled ?? DateTime.MinValue)
            .Take(2000)
            .ToListAsync(ct);

        if (!dueTargets.Any())
        {
            _logger.LogDebug("No targets due for scanning");
            return;
        }

        _logger.LogInformation("Found {Count} targets due for scanning", dueTargets.Count);
        var batchId = Guid.NewGuid().ToString();

        foreach (var target in dueTargets)
        {
            // 1. Score the target and determine cadence
            UpdateInterestScore(target);
            target.Cadence = ComputeCadence(target);

            // 2. Select ports based on cadence
            var ports = await portStrategy.SelectPortsAsync(target, ct);

            // 3. Enqueue the scan job
            var job = new Stage1ScanJob
            {
                BatchId = batchId,
                TargetIp = target.Ip,
                PortsToScan = ports
            };

            var dispatchScheduleResult = await dispatchStateService.ScheduleAsync(
                batchId,
                target.Ip,
                ports,
                WorkerId,
                ct);

            if (dispatchScheduleResult.Status == Stage1DispatchTransitionStatus.Rejected)
            {
                _logger.LogWarning(
                    "Skipping Stage 1 scheduling for {TargetIp} in batch {BatchId}: {Reason}",
                    target.Ip,
                    batchId,
                    dispatchScheduleResult.Reason);
                continue;
            }

            try
            {
                await queue.EnqueueAsync(
                    job,
                    idempotencyKey: $"stage1:{target.Ip}:{batchId}",
                    priority: 0,
                    enqueueOptions: new JobEnqueueOptions { ShardKey = target.Ip },
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                await dispatchStateService.FailAsync(batchId, target.Ip, WorkerId, ex.Message, ct);
                throw;
            }

            // 4. Compute next schedule based on cadence
            target.LastScheduled = now;
            target.NextScheduled = now.Add(CadenceToInterval(target.Cadence));
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Scheduled {Count} targets in batch {BatchId} (ports via {Strategy})",
            dueTargets.Count, batchId, portStrategy.Name);
    }

    /// <summary>
    /// Computes a 0–100 interest score based on how "alive" and "changing" a target is.
    /// </summary>
    private static void UpdateInterestScore(Target target)
    {
        double score = 0;

        // Open ports contribute most to interest
        score += Math.Min(target.OpenPortCount * 10, 40); // max 40 from ports

        // Recent state changes indicate volatility
        score += Math.Min(target.StateChangeCount * 5, 30); // max 30 from changes

        // Recency of last state change
        if (target.LastStateChange.HasValue)
        {
            var daysSinceChange = (DateTime.UtcNow - target.LastStateChange.Value).TotalDays;
            if (daysSinceChange < 1) score += 20;
            else if (daysSinceChange < 7) score += 10;
            else if (daysSinceChange < 30) score += 5;
        }

        target.InterestScore = Math.Clamp(score, 0, 100);
    }

    /// <summary>
    /// Maps interest score to a ScanCadence tier.
    /// </summary>
    private static ScanCadence ComputeCadence(Target target)
    {
        return target.InterestScore switch
        {
            >= 80 => ScanCadence.Critical,
            >= 60 => ScanCadence.Hot,
            >= 30 => ScanCadence.Warm,
            >= 10 => ScanCadence.Standard,
            _ => ScanCadence.Cold
        };
    }

    /// <summary>
    /// Converts ScanCadence to a concrete TimeSpan for NextScheduled.
    /// </summary>
    private static TimeSpan CadenceToInterval(ScanCadence cadence)
    {
        return cadence switch
        {
            ScanCadence.Critical => TimeSpan.FromHours(1),
            ScanCadence.Hot => TimeSpan.FromHours(6),
            ScanCadence.Warm => TimeSpan.FromHours(24),
            ScanCadence.Standard => TimeSpan.FromDays(15),
            ScanCadence.Cold => TimeSpan.FromDays(60),
            _ => TimeSpan.FromDays(15)
        };
    }
}
