namespace Nadosh.Core.Interfaces;

/// <summary>
/// MAC address vendor lookup service using IEEE OUI database.
/// Maps MAC addresses to manufacturer/vendor names and device type hints.
/// </summary>
public interface IMacVendorLookup
{
    /// <summary>
    /// Looks up vendor information for a MAC address.
    /// </summary>
    /// <param name="macAddress">MAC address in any common format (XX:XX:XX:XX:XX:XX, XX-XX-XX-XX-XX-XX, XXXXXXXXXXXX)</param>
    /// <returns>Vendor lookup result or null if not found</returns>
    MacVendorInfo? Lookup(string macAddress);
    
    /// <summary>
    /// Reloads the vendor database from disk (useful for updates).
    /// </summary>
    Task ReloadDatabaseAsync();
}

public class MacVendorInfo
{
    /// <summary>
    /// Manufacturer/vendor name (e.g., "Apple, Inc.", "Tesla, Inc.")
    /// </summary>
    public string Vendor { get; set; } = string.Empty;
    
    /// <summary>
    /// OUI prefix (first 3 bytes) in XX:XX:XX format
    /// </summary>
    public string OuiPrefix { get; set; } = string.Empty;
    
    /// <summary>
    /// Device type hint based on vendor patterns (e.g., "smartphone", "iot", "networking")
    /// </summary>
    public string? DeviceType { get; set; }
    
    /// <summary>
    /// Additional metadata from the OUI database
    /// </summary>
    public string? Comment { get; set; }
}
