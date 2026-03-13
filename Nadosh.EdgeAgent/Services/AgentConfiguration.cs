namespace Nadosh.EdgeAgent.Services;

public class AgentConfiguration
{
    public string MothershipUrl { get; }
    public string ApiKey { get; }
    public string SiteId { get; }
    public string AgentId { get; }
    public string[] WorkerRoles { get; }
    public string Hostname { get; }
    public string Platform { get; }
    public string Version { get; } = "1.0.0";

    public AgentConfiguration()
    {
        MothershipUrl = Environment.GetEnvironmentVariable("NADOSH_MOTHERSHIP_URL") ?? "http://localhost:5000";
        ApiKey = Environment.GetEnvironmentVariable("NADOSH_API_KEY") ?? "";
        SiteId = Environment.GetEnvironmentVariable("NADOSH_SITE_ID") ?? "default-site";
        AgentId = Environment.GetEnvironmentVariable("NADOSH_AGENT_ID") ?? $"{SiteId}-{Guid.NewGuid().ToString()[..8]}";
        
        var rolesEnv = Environment.GetEnvironmentVariable("NADOSH_WORKER_ROLES") ?? "discovery,scanning,monitoring";
        WorkerRoles = rolesEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        
        Hostname = Environment.MachineName;
        Platform = OperatingSystem.IsWindows() ? "windows" 
                 : OperatingSystem.IsLinux() ? "linux" 
                 : OperatingSystem.IsMacOS() ? "macos" 
                 : "unknown";
    }
}
