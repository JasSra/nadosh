using Nadosh.Workers;
using Nadosh.Workers.Edge;
using Nadosh.Workers.Rules;
using Nadosh.Workers.Workers;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Services;
using Nadosh.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddNadoshInfrastructure(builder.Configuration);
builder.Services.AddHttpClient(); // For webhook notifications
builder.Services.AddSingleton<IRuleExecutionService, RuleExecutionService>();
builder.Services.AddSingleton<IAssessmentToolCatalog, DefaultAssessmentToolCatalog>();

if (builder.Configuration.GetValue<bool>("EdgeControlPlane:Enabled"))
{
    builder.Services.AddHostedService<EdgeControlPlaneSyncService>();
}

// CVE enrichment service
builder.Services.AddHttpClient<Nadosh.Core.Services.CveEnrichmentService>();

// Threat scoring and MITRE mapping services
builder.Services.AddScoped<ThreatScoringService>();
builder.Services.AddScoped<MitreAttackMappingService>();
builder.Services.AddScoped<SnmpScannerService>();

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

if (ShouldRun("cve-enrichment"))
    builder.Services.AddHostedService<CveEnrichmentWorker>();

if (ShouldRun("threat-scoring"))
    builder.Services.AddHostedService<ThreatScoringWorker>();

// Keep Stage2Worker for legacy enrichment rules (still fed by classifier)
if (ShouldRun("enrichment") || ShouldRun("all"))
    builder.Services.AddHostedService<Stage2Worker>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
logger.LogInformation(
    "Starting Nadosh workers with roles '{Roles}' and edge control-plane sync {EdgeSyncState}.",
    workerRole,
    builder.Configuration.GetValue<bool>("EdgeControlPlane:Enabled") ? "enabled" : "disabled");

host.Run();
