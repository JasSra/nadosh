using Microsoft.Extensions.Logging;
using Nadosh.Agents.Agents;
using Nadosh.Agents.Models;

Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("║   NADOSH TOOL CAPABILITIES AGENT - DEMO                  ║");
Console.ResetColor();
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// Create logger
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<GetCapabilitiesToolsAgent>();

// Create agent
var agent = new GetCapabilitiesToolsAgent(logger);

Console.WriteLine("🔍 Scanning system for security tools...\n");

// Discover tools
var tools = await agent.DiscoverAvailableToolsAsync();

Console.WriteLine($"✓ Discovery complete: {tools.Count} tools catalogued\n");

// Group by category
var grouped = tools.GroupBy(t => t.Category).OrderBy(g => g.Key.ToString());

foreach (var group in grouped)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"═══ {group.Key} ({group.Count()} tools) ═══");
    Console.ResetColor();
    
    foreach (var tool in group.OrderBy(t => t.ToolName))
    {
        var statusIcon = tool.IsInstalled ? "✓" : "✗";
        var statusColor = tool.IsInstalled ? ConsoleColor.Green : ConsoleColor.Red;
        
        Console.ForegroundColor = statusColor;
        Console.Write($"  {statusIcon} ");
        Console.ResetColor();
        
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"{tool.ToolName,-20}");
        Console.ResetColor();
        
        if (tool.IsInstalled && !string.IsNullOrEmpty(tool.Version))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($" v{tool.Version,-15}");
            Console.ResetColor();
        }
        else
        {
            Console.Write(new string(' ', 17));
        }
        
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($" {tool.Capabilities.Count} capabilities");
        Console.ResetColor();
        
        if (tool.RequiresElevation)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write(" [sudo]");
            Console.ResetColor();
        }
        
        Console.WriteLine();
    }
    Console.WriteLine();
}

// Summary statistics
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"📊 SUMMARY:");
Console.ResetColor();
Console.WriteLine($"   Total tools catalogued:     {tools.Count}");
Console.WriteLine($"   Installed and ready:        {tools.Count(t => t.IsInstalled)}");
Console.WriteLine($"   Not installed:              {tools.Count(t => !t.IsInstalled)}");
Console.WriteLine($"   Require elevation (sudo):   {tools.Count(t => t.RequiresElevation)}");
Console.WriteLine($"   Total capabilities:         {tools.Sum(t => t.Capabilities.Count)}");
Console.WriteLine();

// Show installed tools details
var installedTools = tools.Where(t => t.IsInstalled).ToList();
if (installedTools.Any())
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("═══ INSTALLED TOOLS DETAILS ═══");
    Console.ResetColor();
    Console.WriteLine();
    
    foreach (var tool in installedTools.Take(5))
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"🔧 {tool.ToolName}");
        Console.ResetColor();
        Console.WriteLine($"   Suite:        {tool.ToolSuite}");
        Console.WriteLine($"   Package:      {tool.PackageName}");
        Console.WriteLine($"   Version:      {tool.Version ?? "Unknown"}");
        Console.WriteLine($"   Executable:   {tool.ExecutablePath ?? "N/A"}");
        Console.WriteLine($"   Usage:        {tool.UsageHint}");
        Console.Write($"   Capabilities: ");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(string.Join(", ", tool.Capabilities));
        Console.ResetColor();
        Console.Write($"   Common flags: ");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(string.Join(" ", tool.CommonFlags.Take(5)));
        Console.ResetColor();
        Console.WriteLine();
    }
    
    if (installedTools.Count > 5)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"... and {installedTools.Count - 5} more installed tools");
        Console.ResetColor();
        Console.WriteLine();
    }
}

// Show capabilities breakdown
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("═══ CAPABILITY BREAKDOWN ═══");
Console.ResetColor();

var allCapabilities = tools
    .SelectMany(t => t.Capabilities)
    .GroupBy(c => c)
    .OrderByDescending(g => g.Count())
    .Take(10);

foreach (var cap in allCapabilities)
{
    Console.Write($"   {cap.Key,-30}");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"{cap.Count()} tools");
    Console.ResetColor();
}

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("✓ Demo complete!");
Console.ResetColor();
