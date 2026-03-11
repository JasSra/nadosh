using System.Net.Sockets;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;
using Nadosh.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Nadosh.Workers.Queue;

namespace Nadosh.Workers;

public class Stage1Worker : BackgroundService
{
    private static readonly string WorkerId = $"{Environment.MachineName}/stage1/{Environment.ProcessId}";
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Stage1Worker> _logger;
    private readonly IJobQueue<ClassificationJob> _classificationQueue;

    public Stage1Worker(IServiceProvider serviceProvider, ILogger<Stage1Worker> logger, IJobQueue<ClassificationJob> classificationQueue)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _classificationQueue = classificationQueue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Stage1Worker starting...");
        
        using var scope = _serviceProvider.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue<Stage1ScanJob>>();
        var queuePolicy = scope.ServiceProvider.GetRequiredService<IQueuePolicyProvider>().GetPolicy<Stage1ScanJob>();

        while (!stoppingToken.IsCancellationRequested)
        {
            var msg = await queue.DequeueAsync(queuePolicy.VisibilityTimeout, stoppingToken);
            if (msg == null)
            {
                await Task.Delay(2000, stoppingToken);
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
                _logger.LogError(ex, $"Error processing Stage 1 job for IP {msg.Payload.TargetIp}");
                await QueueProcessingUtilities.RejectWithBackoffOrDeadLetterAsync(queue, msg, ex, _logger, queuePolicy, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(Stage1ScanJob job, IServiceProvider scopedProvider, CancellationToken ct)
    {
        _logger.LogInformation($"Scanning target {job.TargetIp} on ports {string.Join(",", job.PortsToScan)}");
        
        var observations = new List<Observation>();
        foreach (var port in job.PortsToScan)
        {
            var obs = await ProbePortAsync(job.TargetIp, port, ct);
            obs.ScanRunId = job.BatchId;
            observations.Add(obs);
        }

        var db = scopedProvider.GetRequiredService<NadoshDbContext>();
        var handoffDispatchService = scopedProvider.GetRequiredService<IObservationHandoffDispatchService>();
        db.Observations.AddRange(observations);
        await db.SaveChangesAsync(ct);

        // Queue for classification
        foreach (var obs in observations)
        {
            await handoffDispatchService.ScheduleAsync(
                ObservationHandoffDispatchKind.Classification,
                obs.Id,
                job.BatchId,
                obs.TargetId,
                obs.Port,
                obs.Protocol,
                obs.ServiceName,
                WorkerId,
                ct);

            try
            {
                await _classificationQueue.EnqueueAsync(
                    new ClassificationJob { Observation = obs },
                    idempotencyKey: $"clf:{obs.Id}",
                    priority: 0,
                    enqueueOptions: new JobEnqueueOptions { ShardKey = obs.TargetId },
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                await handoffDispatchService.FailAsync(
                    ObservationHandoffDispatchKind.Classification,
                    obs.Id,
                    WorkerId,
                    ex.Message,
                    cancellationToken: ct);
                throw;
            }
        }
    }

    private async Task<Observation> ProbePortAsync(string ip, int port, CancellationToken ct)
    {
        var obs = new Observation
        {
            TargetId = ip,
            ObservedAt = DateTime.UtcNow,
            Port = port,
            Protocol = "tcp",
            State = "closed"
        };

        try
        {
            using var tcpClient = new TcpClient();
            var connectTask = tcpClient.ConnectAsync(ip, port, ct);
            if (await Task.WhenAny(connectTask.AsTask(), Task.Delay(3000, ct)) == connectTask.AsTask())
            {
                if (tcpClient.Connected)
                {
                    obs.State = "open";
                }
            }
            else
            {
                obs.State = "filtered";
            }
        }
        catch (SocketException)
        {
            obs.State = "closed";
        }
        catch (OperationCanceledException)
        {
            obs.State = "filtered";
        }

        return obs;
    }
}
