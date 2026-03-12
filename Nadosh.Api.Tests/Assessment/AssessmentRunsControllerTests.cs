using System.Net;
using System.Net.Http.Json;
using Nadosh.Api.Controllers;
using Nadosh.Api.Tests.Infrastructure;
using Nadosh.Core.Models;
using Nadosh.Infrastructure.Data;

namespace Nadosh.Api.Tests.Assessment;

public sealed class AssessmentRunsControllerTests
{
    [Fact]
    public async Task Submit_ReturnsAccepted_ForApprovalPendingRun()
    {
        using var factory = new AssessmentApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", AssessmentApiFactory.ApiKey);

        var response = await client.PostAsJsonAsync("/v1/AssessmentRuns", new
        {
            toolId = "service.http.metadata.collect",
            requestedBy = "operator@example.com",
            targetScope = "198.51.100.25:8080",
            scopeKind = AssessmentScopeKind.Service,
            environment = AssessmentExecutionEnvironment.ExternalAuthorized,
            scopeTags = new[] { "authorized-scope", "http" }
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AssessmentRunResponse>();
        Assert.NotNull(payload);
        Assert.Equal(AssessmentRunStatus.AwaitingApproval, payload!.Status);
        Assert.True(payload.RequiresApproval);
    }

    [Fact]
    public async Task GetAgentContext_ReturnsAdapterContext()
    {
        using var factory = new AssessmentApiFactory();
        await factory.SeedAsync(async dbContext =>
        {
            dbContext.AssessmentRuns.Add(new AssessmentRun
            {
                RunId = "run-agent-context",
                ToolId = "validation.workflow.dry-run",
                RequestedBy = "analyst@example.com",
                TargetScope = "lab/web-app",
                ScopeKind = AssessmentScopeKind.Workflow,
                Environment = AssessmentExecutionEnvironment.Lab,
                DryRun = true,
                Status = AssessmentRunStatus.Queued,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                SubmittedAt = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", AssessmentApiFactory.ApiKey);

        var response = await client.GetAsync("/v1/AssessmentRuns/run-agent-context/agent-context");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<MicrosoftAgentAssessmentContext>();
        Assert.NotNull(payload);
        Assert.Equal("assessment-run:run-agent-context", payload!.SessionId);
        Assert.Contains(payload.Tools, tool => tool.IsPrimaryTool && tool.ToolId == "validation.workflow.dry-run");
    }

    [Fact]
    public async Task GetEvidence_ReturnsEvidenceBundle()
    {
        using var factory = new AssessmentApiFactory();
        await factory.SeedAsync(async dbContext =>
        {
            dbContext.AssessmentRuns.Add(new AssessmentRun
            {
                RunId = "run-evidence",
                ToolId = "exposure.query.current",
                RequestedBy = "analyst@example.com",
                TargetScope = "203.0.113.77",
                ScopeKind = AssessmentScopeKind.IpAddress,
                Environment = AssessmentExecutionEnvironment.InternalAuthorized,
                Status = AssessmentRunStatus.Completed,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                SubmittedAt = DateTime.UtcNow
            });

            dbContext.Targets.Add(new Target { Ip = "203.0.113.77", Monitored = true });

            dbContext.CurrentExposures.Add(new CurrentExposure
            {
                Id = 100,
                TargetId = "203.0.113.77",
                Port = 443,
                Protocol = "tcp",
                CurrentState = "open",
                FirstSeen = DateTime.UtcNow.AddDays(-10),
                LastSeen = DateTime.UtcNow,
                LastChanged = DateTime.UtcNow,
                Classification = "https",
                Severity = "medium"
            });

            dbContext.Observations.Add(new Observation
            {
                Id = 200,
                TargetId = "203.0.113.77",
                ObservedAt = DateTime.UtcNow,
                Port = 443,
                Protocol = "tcp",
                State = "open",
                ServiceName = "https"
            });

            dbContext.EnrichmentResults.Add(new EnrichmentResult
            {
                Id = 300,
                CurrentExposureId = 100,
                ObservationId = 200,
                RuleId = "http-title-check",
                RuleVersion = "1.0.0",
                ResultStatus = "success",
                Summary = "Collected HTTPS metadata",
                ExecutedAt = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", AssessmentApiFactory.ApiKey);

        var response = await client.GetAsync("/v1/AssessmentRuns/run-evidence/evidence");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AssessmentEvidenceBundle>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.ExposureCount);
        Assert.Equal(1, payload.ObservationCount);
        Assert.Equal(1, payload.EnrichmentCount);
    }

    [Fact]
    public async Task List_ReturnsRunsByStatus()
    {
        using var factory = new AssessmentApiFactory();
        await factory.SeedAsync(async dbContext =>
        {
            dbContext.AssessmentRuns.AddRange(
                new AssessmentRun
                {
                    RunId = "queued-run",
                    ToolId = "validation.workflow.dry-run",
                    RequestedBy = "analyst@example.com",
                    TargetScope = "lab/api",
                    ScopeKind = AssessmentScopeKind.Workflow,
                    Environment = AssessmentExecutionEnvironment.Lab,
                    Status = AssessmentRunStatus.Queued,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new AssessmentRun
                {
                    RunId = "completed-run",
                    ToolId = "exposure.query.current",
                    RequestedBy = "analyst@example.com",
                    TargetScope = "203.0.113.88",
                    ScopeKind = AssessmentScopeKind.IpAddress,
                    Environment = AssessmentExecutionEnvironment.InternalAuthorized,
                    Status = AssessmentRunStatus.Completed,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });

            await dbContext.SaveChangesAsync();
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", AssessmentApiFactory.ApiKey);

        var response = await client.GetAsync("/v1/AssessmentRuns?status=Queued");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AssessmentRunListResponse>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Count);
        Assert.Single(payload.Results);
        Assert.Equal("queued-run", payload.Results.Single().RunId);
    }
}
