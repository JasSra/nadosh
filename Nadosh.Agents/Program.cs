using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Nadosh.Agents.Agents;
using Nadosh.Agents.Orchestration;
using Nadosh.Agents.Workers;
using Nadosh.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Nadosh.Core.Interfaces;

var builder = Host.CreateApplicationBuilder(args);

// Logging configuration
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Add database context
var connectionString = builder.Configuration["ConnectionStrings:DefaultConnection"] 
    ?? "Host=localhost;Port=5439;Database=nadosh;Username=nadosh;Password=nadosh_password";

builder.Services.AddDbContext<NadoshDbContext>(options =>
    options.UseNpgsql(connectionString));

// Register repositories and services manually
builder.Services.AddScoped<IAssessmentRunRepository, AssessmentRunRepository>();
builder.Services.AddScoped<IAssessmentEvidenceService, AssessmentEvidenceService>();
builder.Services.AddScoped<IAssessmentPolicyService, Nadosh.Core.Services.AssessmentPolicyService>();
builder.Services.AddScoped<IAssessmentToolCatalog, Nadosh.Core.Services.DefaultAssessmentToolCatalog>();

// Configure Semantic Kernel for AI orchestration
var openAiKey = builder.Configuration["OpenAI:ApiKey"] 
    ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var openAiModel = builder.Configuration["OpenAI:Model"] ?? "gpt-4o";

if (string.IsNullOrEmpty(openAiKey))
{
    throw new InvalidOperationException(
        "OpenAI API key not configured. Set OpenAI:ApiKey in appsettings.json or OPENAI_API_KEY environment variable.");
}

builder.Services.AddKernel()
    .AddOpenAIChatCompletion(openAiModel, openAiKey);

// Register agents
builder.Services.AddSingleton<GetCapabilitiesToolsAgent>();
builder.Services.AddScoped<ExecuteCommandAgent>();
builder.Services.AddScoped<ParseAndPlanAgent>();

// Register orchestration engine
builder.Services.AddScoped<PhaseOrchestrationEngine>();

// Register background worker
builder.Services.AddHostedService<AgentAssessmentWorker>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
logger.LogInformation("Nadosh.Agents starting - AI-powered penetration testing orchestration");
logger.LogInformation("OpenAI Model: {Model}", openAiModel);

await host.RunAsync();
