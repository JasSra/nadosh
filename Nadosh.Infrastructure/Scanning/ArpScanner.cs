using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;
using Microsoft.Extensions.Logging;

namespace Nadosh.Infrastructure.Scanning;

/// <summary>
/// ARP (Address Resolution Protocol) scanner for discovering MAC addresses on local networks.
/// Uses platform-specific ARP commands (arp -a on Windows, ip neigh on Linux).
/// </summary>
public class ArpScanner
{
    private readonly ILogger<ArpScanner> _logger;
    private readonly IJobQueue<MacEnrichmentJob> _macQueue;

    public ArpScanner(ILogger<ArpScanner> logger, IJobQueue<MacEnrichmentJob> macQueue)
    {
        _logger = logger;
        _macQueue = macQueue;
    }

    /// <summary>
    /// Attempts to resolve MAC address for a target IP using ARP.
    /// First sends a ping to ensure ARP cache is populated, then queries the ARP table.
    /// </summary>
    public async Task<string?> ResolveMacAddressAsync(string ipAddress, CancellationToken ct = default)
    {
        try
        {
            // Ping the target to populate ARP cache (non-blocking)
            using var ping = new Ping();
            try
            {
                await ping.SendPingAsync(ipAddress, 500); // 500ms timeout
            }
            catch
            {
                // Ping might fail but ARP entry could still be created
            }

            // Query ARP table
            var macAddress = await QueryArpTableAsync(ipAddress, ct);
            
            if (!string.IsNullOrEmpty(macAddress))
            {
                _logger.LogDebug("Resolved MAC for {Ip}: {Mac}", ipAddress, macAddress);
                
                // Queue for enrichment
                await _macQueue.EnqueueAsync(new MacEnrichmentJob
                {
                    TargetIp = ipAddress,
                    MacAddress = macAddress
                },
                idempotencyKey: $"mac:{ipAddress}:{macAddress}",
                priority: 0,
                enqueueOptions: new JobEnqueueOptions { ShardKey = ipAddress },
                cancellationToken: ct);
                
                return macAddress;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve MAC for {Ip}", ipAddress);
            return null;
        }
    }

    private async Task<string?> QueryArpTableAsync(string ipAddress, CancellationToken ct)
    {
        try
        {
            var (command, args) = GetArpCommand(ipAddress);
            
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
                return null;

            return ParseArpOutput(output, ipAddress);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error querying ARP table for {Ip}", ipAddress);
            return null;
        }
    }

    private static (string command, string args) GetArpCommand(string ipAddress)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ("arp", $"-a {ipAddress}");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return ("ip", $"neigh show {ipAddress}");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return ("arp", $"-n {ipAddress}");
        }
        else
        {
            throw new PlatformNotSupportedException("ARP scanning not supported on this platform");
        }
    }

    private string? ParseArpOutput(string output, string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            // Windows format: "192.168.1.1    00-1a-2b-3c-4d-5e     dynamic"
            // Linux format:   "192.168.1.1 dev eth0 lladdr 00:1a:2b:3c:4d:5e REACHABLE"
            
            if (!line.Contains(ipAddress))
                continue;

            // Extract MAC address using regex patterns
            var macPatterns = new[]
            {
                @"([0-9A-Fa-f]{2}[-:]){5}[0-9A-Fa-f]{2}", // XX:XX:XX:XX:XX:XX or XX-XX-XX-XX-XX-XX
                @"([0-9A-Fa-f]{4}\.){2}[0-9A-Fa-f]{4}"    // XXXX.XXXX.XXXX (Cisco format)
            };

            foreach (var pattern in macPatterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(line, pattern);
                if (match.Success)
                {
                    return NormalizeMacAddress(match.Value);
                }
            }
        }

        return null;
    }

    private static string NormalizeMacAddress(string mac)
    {
        // Convert to XX:XX:XX:XX:XX:XX format
        var cleaned = System.Text.RegularExpressions.Regex.Replace(mac, "[^0-9A-Fa-f]", "");
        
        if (cleaned.Length != 12)
            return mac; // Return as-is if invalid length

        return string.Join(":", Enumerable.Range(0, 6)
            .Select(i => cleaned.Substring(i * 2, 2).ToUpperInvariant()));
    }
}
