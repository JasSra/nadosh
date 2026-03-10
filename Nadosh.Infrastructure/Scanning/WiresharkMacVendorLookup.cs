using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Nadosh.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Nadosh.Infrastructure.Scanning;

/// <summary>
/// MAC vendor lookup using Wireshark's manuf database format.
/// Loads OUI (Organizationally Unique Identifier) mappings from a local file.
/// Database can be downloaded from: https://gitlab.com/wireshark/wireshark/-/raw/master/manuf
/// </summary>
public class WiresharkMacVendorLookup : IMacVendorLookup
{
    private readonly ILogger<WiresharkMacVendorLookup> _logger;
    private readonly string _databasePath;
    private ConcurrentDictionary<string, MacVendorInfo> _ouiDatabase = new();
    
    // Common device type patterns based on vendor names
    private static readonly Dictionary<string, string> DeviceTypePatterns = new()
    {
        { "apple", "smartphone/tablet/laptop" },
        { "samsung", "smartphone/tablet" },
        { "google", "smartphone/iot" },
        { "tesla", "vehicle/iot" },
        { "cisco", "networking" },
        { "ubiquiti", "networking" },
        { "netgear", "networking" },
        { "tp-link", "networking" },
        { "sonos", "iot/audio" },
        { "nest", "iot/home-automation" },
        { "ring", "iot/security" },
        { "amazon", "iot/smart-speaker" },
        { "philips", "iot/lighting" },
        { "raspberry", "embedded/iot" },
        { "intel", "laptop/server" },
        { "dell", "laptop/server" },
        { "hp", "laptop/printer" },
        { "lenovo", "laptop" },
        { "microsoft", "laptop/tablet" },
        { "vmware", "virtual-machine" },
        { "qemu", "virtual-machine" },
        { "synology", "nas" },
        { "western digital", "nas" }
    };

    public WiresharkMacVendorLookup(ILogger<WiresharkMacVendorLookup> logger, string? databasePath = null)
    {
        _logger = logger;
        _databasePath = databasePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "manuf");
        
        // Load database on initialization
        _ = ReloadDatabaseAsync();
    }

    public MacVendorInfo? Lookup(string macAddress)
    {
        var normalized = NormalizeMacAddress(macAddress);
        if (string.IsNullOrEmpty(normalized))
            return null;

        // Try 24-bit OUI (first 3 bytes) - most common
        var oui24 = normalized[..8]; // XX:XX:XX
        if (_ouiDatabase.TryGetValue(oui24, out var vendor))
            return vendor;

        // Try 28-bit OUI (rare but exists)
        if (normalized.Length >= 10)
        {
            var oui28 = normalized[..10]; // XX:XX:X
            if (_ouiDatabase.TryGetValue(oui28, out vendor))
                return vendor;
        }

        // Try 36-bit OUI (very rare)
        if (normalized.Length >= 14)
        {
            var oui36 = normalized[..14]; // XX:XX:XX:XX:X
            if (_ouiDatabase.TryGetValue(oui36, out vendor))
                return vendor;
        }

        return null;
    }

    public async Task ReloadDatabaseAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var newDatabase = new ConcurrentDictionary<string, MacVendorInfo>();

        try
        {
            if (!File.Exists(_databasePath))
            {
                _logger.LogWarning("MAC vendor database not found at {Path}. Download from: https://gitlab.com/wireshark/wireshark/-/raw/master/manuf", 
                    _databasePath);
                return;
            }

            int lineCount = 0;
            int validEntries = 0;

            await foreach (var line in File.ReadLinesAsync(_databasePath))
            {
                lineCount++;
                
                // Skip comments and empty lines
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                var prefix = parts[0].Trim();
                var vendorName = parts[1].Trim();
                var comment = parts.Length > 2 ? parts[2].Trim() : null;

                // Normalize prefix to XX:XX:XX format
                var normalizedPrefix = NormalizeMacPrefix(prefix);
                if (string.IsNullOrEmpty(normalizedPrefix))
                    continue;

                var deviceType = InferDeviceType(vendorName, comment);

                newDatabase[normalizedPrefix] = new MacVendorInfo
                {
                    Vendor = vendorName,
                    OuiPrefix = normalizedPrefix,
                    DeviceType = deviceType,
                    Comment = comment
                };

                validEntries++;
            }

            _ouiDatabase = newDatabase;
            _logger.LogInformation("Loaded {ValidEntries} MAC vendor entries from {Path} ({Lines} lines) in {Ms}ms",
                validEntries, _databasePath, lineCount, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load MAC vendor database from {Path}", _databasePath);
        }
    }

    private static string? NormalizeMacAddress(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
            return null;

        // Remove all separators and convert to uppercase
        var cleaned = Regex.Replace(mac, "[^0-9A-Fa-f]", "").ToUpperInvariant();
        
        if (cleaned.Length < 6)
            return null;

        // Convert to XX:XX:XX:XX:XX:XX format
        var normalized = string.Join(":", Enumerable.Range(0, Math.Min(6, cleaned.Length / 2))
            .Select(i => cleaned.Substring(i * 2, 2)));

        return normalized;
    }

    private static string? NormalizeMacPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return null;

        // Handle CIDR notation (e.g., "00:00:00:00:00:00/36")
        var cidrIndex = prefix.IndexOf('/');
        if (cidrIndex > 0)
            prefix = prefix[..cidrIndex];

        // Remove all separators and convert to uppercase
        var cleaned = Regex.Replace(prefix, "[^0-9A-Fa-f]", "").ToUpperInvariant();
        
        if (cleaned.Length < 6)
            return null;

        // Pad to ensure we have enough hex digits
        if (cleaned.Length % 2 != 0)
            cleaned += "0";

        // Convert to XX:XX:XX format (for 24-bit OUI)
        var normalized = string.Join(":", Enumerable.Range(0, cleaned.Length / 2)
            .Select(i => cleaned.Substring(i * 2, 2)));

        return normalized;
    }

    private static string? InferDeviceType(string vendorName, string? comment)
    {
        var searchText = (vendorName + " " + (comment ?? "")).ToLowerInvariant();

        foreach (var (pattern, deviceType) in DeviceTypePatterns)
        {
            if (searchText.Contains(pattern))
                return deviceType;
        }

        return null;
    }
}
