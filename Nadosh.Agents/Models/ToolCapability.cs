namespace Nadosh.Agents.Models;

/// <summary>
/// Represents a discovered pentest tool capability within the container environment.
/// </summary>
public class ToolCapability
{
    public required string ToolName { get; set; }
    public required string ExecutablePath { get; set; }
    public string? Version { get; set; }
    public required ToolCategory Category { get; set; }
    public string? ToolSuite { get; set; }
    public string? PackageName { get; set; }
    public List<string> Capabilities { get; set; } = new();
    public List<string> CommonFlags { get; set; } = new();
    public List<string> Aliases { get; set; } = new();
    public string? UsageHint { get; set; }
    public bool RequiresElevation { get; set; }
    public List<string> RequiredDependencies { get; set; } = new();
}

public enum ToolCategory
{
    Discovery,           // Initial reconnaissance and asset discovery
    Enumeration,         // Deep enumeration of services, users, shares
    Vulnerability,       // Vulnerability scanning and correlation
    WebScanning,         // Web application security testing
    NetworkScanning,     // Network-level scanning and mapping
    PasswordAttack,      // Password cracking and brute-forcing
    Exploitation,        // Exploit frameworks and modules
    PostExploitation,
    Forensics,
    Utility
}
