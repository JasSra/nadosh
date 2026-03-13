using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nadosh.EdgeAgent.Services;

namespace Nadosh.EdgeAgent;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            // Logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            // HTTP client for mothership communication
            builder.Services.AddHttpClient("Mothership", client =>
            {
                var mothershipUrl = Environment.GetEnvironmentVariable("NADOSH_MOTHERSHIP_URL") 
                    ?? builder.Configuration["Mothership:Url"] 
                    ?? "http://localhost:5000";
                
                client.BaseAddress = new Uri(mothershipUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
                
                var apiKey = Environment.GetEnvironmentVariable("NADOSH_API_KEY") 
                    ?? builder.Configuration["Mothership:ApiKey"];
                
                if (!string.IsNullOrEmpty(apiKey))
                {
                    client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
                }
            });

            // Register services
            builder.Services.AddSingleton<AgentConfiguration>();
            builder.Services.AddSingleton<EnrollmentService>();
            builder.Services.AddHostedService<HeartbeatWorker>();
            builder.Services.AddHostedService<TaskExecutionWorker>();

            var host = builder.Build();

            // Display startup banner
            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            var config = host.Services.GetRequiredService<AgentConfiguration>();
            
            logger.LogInformation("═══════════════════════════════════════════════");
            logger.LogInformation("  Nadosh Edge Agent v1.0.0");
            logger.LogInformation("═══════════════════════════════════════════════");
            logger.LogInformation("Mothership: {Mothership}", config.MothershipUrl);
            logger.LogInformation("Site: {SiteId}", config.SiteId);
            logger.LogInformation("Agent: {AgentId}", config.AgentId);
            logger.LogInformation("Roles: {Roles}", string.Join(", ", config.WorkerRoles));
            logger.LogInformation("═══════════════════════════════════════════════");

            // Enroll with mothership
            var enrollment = host.Services.GetRequiredService<EnrollmentService>();
            if (!await enrollment.EnrollAsync())
            {
                logger.LogError("Failed to enroll with mothership. Exiting.");
                return 1;
            }

            await host.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }
}
