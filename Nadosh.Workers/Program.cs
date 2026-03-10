using Nadosh.Workers;
using Nadosh.Workers.Workers;
using Nadosh.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddNadoshInfrastructure(builder.Configuration);
builder.Services.AddHttpClient(); // For webhook notifications

// Role-based worker selection via WORKER_ROLE env var.
// Values: "all" (default), "discovery", "banner", "fingerprint", "classifier", "scheduler"
// Multiple roles can be comma-separated: "discovery,banner"
var workerRole = Environment.GetEnvironmentVariable("WORKER_ROLE")?.ToLowerInvariant() ?? "all";
var roles = workerRole.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();

bool ShouldRun(string role) => roles.Contains("all") || roles.Contains(role);

if (ShouldRun("scheduler"))
    builder.Services.AddHostedService<SchedulerService>();

if (ShouldRun("discovery"))
    builder.Services.AddHostedService<DiscoveryWorker>();

if (ShouldRun("banner"))
    builder.Services.AddHostedService<BannerGrabWorker>();

if (ShouldRun("fingerprint"))
    builder.Services.AddHostedService<FingerprintWorker>();

if (ShouldRun("classifier"))
    builder.Services.AddHostedService<ClassifierWorker>();

if (ShouldRun("cache-projector"))
    builder.Services.AddHostedService<CacheProjectorWorker>();

if (ShouldRun("geo-enrichment"))
    builder.Services.AddHostedService<EnrichmentWorker>();

if (ShouldRun("change-detector"))
    builder.Services.AddHostedService<ChangeDetectorWorker>();

if (ShouldRun("mac-enrichment"))
    builder.Services.AddHostedService<MacEnrichmentWorker>();

// Keep Stage2Worker for legacy enrichment rules (still fed by classifier)
if (ShouldRun("enrichment") || ShouldRun("all"))
    builder.Services.AddHostedService<Stage2Worker>();

var host = builder.Build();
host.Run();
