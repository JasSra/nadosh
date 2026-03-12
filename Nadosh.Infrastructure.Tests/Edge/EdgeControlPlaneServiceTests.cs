using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nadosh.Core.Configuration;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;
using Nadosh.Infrastructure.Data;

namespace Nadosh.Infrastructure.Tests.Edge;

public sealed class EdgeControlPlaneServiceTests
{
    [Fact]
    public async Task ClaimTaskAsync_BindsClaimToAgentAndSetsLeaseExpiry()
    {
        await using var dbContext = CreateDbContext();
        dbContext.EdgeSites.Add(new EdgeSite
        {
            SiteId = "site-1",
            Name = "Site 1",
            IsEnabled = true,
            AllowedCapabilities = ["role:discovery"]
        });
        dbContext.EdgeAgents.Add(new EdgeAgent
        {
            AgentId = "agent-1",
            SiteId = "site-1",
            Status = EdgeAgentStatus.Active,
            AdvertisedCapabilities = ["role:discovery"]
        });
        dbContext.AuthorizedTasks.Add(new AuthorizedTask
        {
            TaskId = "task-1",
            SiteId = "site-1",
            TaskKind = AuthorizedTaskKinds.Stage1Scan,
            PayloadJson = "{}",
            ScopeJson = "{}",
            RequiredCapabilities = ["role:discovery"],
            Status = AuthorizedTaskStatus.Queued
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var beforeClaim = DateTime.UtcNow;

        var response = await service.ClaimTaskAsync("agent-1", "task-1", new EdgeTaskClaimRequest { SiteId = "site-1" });
        var persistedTask = await dbContext.AuthorizedTasks.SingleAsync(task => task.TaskId == "task-1");

        Assert.Equal("agent-1", response.AgentId);
        Assert.Equal("agent-1", persistedTask.ClaimedByAgentId);
        Assert.Equal(AuthorizedTaskStatus.Claimed, persistedTask.Status);
        Assert.False(string.IsNullOrWhiteSpace(persistedTask.LeaseToken));
        Assert.NotNull(persistedTask.LeaseExpiresAt);
        Assert.True(persistedTask.LeaseExpiresAt > beforeClaim.AddMinutes(4));
        Assert.Equal(persistedTask.LeaseExpiresAt, response.LeaseExpiresAt);
    }

    [Fact]
    public async Task RecordHeartbeatAsync_RenewsClaimedLeasesForAgent()
    {
        await using var dbContext = CreateDbContext();
        dbContext.EdgeSites.Add(new EdgeSite
        {
            SiteId = "site-1",
            Name = "Site 1",
            IsEnabled = true
        });
        dbContext.EdgeAgents.Add(new EdgeAgent
        {
            AgentId = "agent-1",
            SiteId = "site-1",
            Status = EdgeAgentStatus.Active,
            AdvertisedCapabilities = ["role:discovery"]
        });

        var originalLeaseExpiry = DateTime.UtcNow.AddMinutes(1);
        dbContext.AuthorizedTasks.Add(new AuthorizedTask
        {
            TaskId = "task-1",
            SiteId = "site-1",
            TaskKind = AuthorizedTaskKinds.Stage1Scan,
            PayloadJson = "{}",
            ScopeJson = "{}",
            Status = AuthorizedTaskStatus.Claimed,
            ClaimedAt = DateTime.UtcNow.AddMinutes(-2),
            ClaimedByAgentId = "agent-1",
            LeaseToken = "lease-token",
            LeaseExpiresAt = originalLeaseExpiry
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var response = await service.RecordHeartbeatAsync(new EdgeHeartbeatRequest
        {
            AgentId = "agent-1",
            SiteId = "site-1",
            Hostname = "edge-host",
            OperatingSystem = "Linux",
            Architecture = "X64",
            AgentVersion = "1.0.0",
            Capabilities = ["role:discovery"]
        }, remoteAddress: "10.0.0.10");

        var persistedTask = await dbContext.AuthorizedTasks.SingleAsync(task => task.TaskId == "task-1");

        Assert.Equal(1, response.RenewedLeaseCount);
        Assert.NotNull(persistedTask.LeaseExpiresAt);
        Assert.True(persistedTask.LeaseExpiresAt > originalLeaseExpiry);
    }

    [Fact]
    public async Task GetPendingTasksAsync_RequeuesExpiredClaimAndMakesItVisibleAgain()
    {
        await using var dbContext = CreateDbContext();
        dbContext.EdgeSites.Add(new EdgeSite
        {
            SiteId = "site-1",
            Name = "Site 1",
            IsEnabled = true
        });
        dbContext.EdgeAgents.Add(new EdgeAgent
        {
            AgentId = "agent-1",
            SiteId = "site-1",
            Status = EdgeAgentStatus.Active,
            AdvertisedCapabilities = ["role:discovery"]
        });
        dbContext.AuthorizedTasks.Add(new AuthorizedTask
        {
            TaskId = "task-1",
            SiteId = "site-1",
            TaskKind = AuthorizedTaskKinds.Stage1Scan,
            PayloadJson = "{}",
            ScopeJson = "{}",
            Status = AuthorizedTaskStatus.Claimed,
            ClaimedAt = DateTime.UtcNow.AddMinutes(-10),
            ClaimedByAgentId = "agent-1",
            LeaseToken = "expired-lease",
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            RequiredCapabilities = ["role:discovery"]
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var pendingTasks = await service.GetPendingTasksAsync("agent-1");
        var persistedTask = await dbContext.AuthorizedTasks.SingleAsync(task => task.TaskId == "task-1");

        Assert.Single(pendingTasks);
        Assert.Equal("task-1", pendingTasks[0].TaskId);
        Assert.Equal(AuthorizedTaskStatus.Queued, persistedTask.Status);
        Assert.Null(persistedTask.ClaimedByAgentId);
        Assert.Null(persistedTask.LeaseToken);
        Assert.Null(persistedTask.LeaseExpiresAt);
    }

    [Fact]
    public async Task GetPendingTasksAsync_FailsExpiredClaimWhenTaskWindowAlsoExpired()
    {
        await using var dbContext = CreateDbContext();
        dbContext.EdgeSites.Add(new EdgeSite
        {
            SiteId = "site-1",
            Name = "Site 1",
            IsEnabled = true
        });
        dbContext.EdgeAgents.Add(new EdgeAgent
        {
            AgentId = "agent-1",
            SiteId = "site-1",
            Status = EdgeAgentStatus.Active
        });
        dbContext.AuthorizedTasks.Add(new AuthorizedTask
        {
            TaskId = "task-1",
            SiteId = "site-1",
            TaskKind = AuthorizedTaskKinds.Stage1Scan,
            PayloadJson = "{}",
            ScopeJson = "{}",
            Status = AuthorizedTaskStatus.Claimed,
            ClaimedAt = DateTime.UtcNow.AddMinutes(-10),
            ClaimedByAgentId = "agent-1",
            LeaseToken = "expired-lease",
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var pendingTasks = await service.GetPendingTasksAsync("agent-1");
        var persistedTask = await dbContext.AuthorizedTasks.SingleAsync(task => task.TaskId == "task-1");

        Assert.Empty(pendingTasks);
        Assert.Equal(AuthorizedTaskStatus.Failed, persistedTask.Status);
        Assert.Null(persistedTask.ClaimedByAgentId);
        Assert.NotNull(persistedTask.CompletedAt);
    }

    private static EdgeControlPlaneService CreateService(NadoshDbContext dbContext)
        => new(
            dbContext,
            new RecordingAuditService(),
            Options.Create(new EdgeControlPlaneOptions
            {
                HeartbeatIntervalSeconds = 30,
                TaskLeaseDurationSeconds = 300
            }));

    private static NadoshDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<NadoshDbContext>()
            .UseInMemoryDatabase($"edge-control-plane-tests-{Guid.NewGuid():N}")
            .Options;

        return new NadoshDbContext(options);
    }

    private sealed class RecordingAuditService : IAuditService
    {
        public Task WriteAsync(
            string actor,
            string action,
            string entityType,
            string entityId,
            object? oldValue = null,
            object? newValue = null,
            object? metadata = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
