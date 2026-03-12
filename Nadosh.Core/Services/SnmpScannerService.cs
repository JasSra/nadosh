using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Nadosh.Core.Services;

/// <summary>
/// SNMP (Simple Network Management Protocol) scanner for network device discovery.
/// Supports SNMP v1 and v2c with community string authentication.
/// Queries standard OIDs for device information, uptime, and network interfaces.
/// </summary>
public class SnmpScannerService
{
    private readonly ILogger<SnmpScannerService> _logger;
    
    // Standard SNMP community strings to try
    private static readonly string[] DefaultCommunities = { "public", "private", "community" };
    
    // Standard OIDs for device information
    private static class Oids
    {
        public const string SysDescr = "1.3.6.1.2.1.1.1.0";       // System description
        public const string SysObjectId = "1.3.6.1.2.1.1.2.0";    // System object ID
        public const string SysUpTime = "1.3.6.1.2.1.1.3.0";      // System uptime
        public const string SysContact = "1.3.6.1.2.1.1.4.0";     // System contact
        public const string SysName = "1.3.6.1.2.1.1.5.0";        // System name
        public const string SysLocation = "1.3.6.1.2.1.1.6.0";    // System location
        public const string SysServices = "1.3.6.1.2.1.1.7.0";    // System services
        public const string IfNumber = "1.3.6.1.2.1.2.1.0";       // Number of interfaces
    }

    public SnmpScannerService(ILogger<SnmpScannerService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Probe an IP address for SNMP service and attempt to enumerate device information
    /// </summary>
    public async Task<SnmpScanResult> ScanAsync(string ipAddress, int port = 161, int timeoutMs = 3000, CancellationToken ct = default)
    {
        var result = new SnmpScanResult { IpAddress = ipAddress, Port = port };

        try
        {
            // Try common community strings
            foreach (var community in DefaultCommunities)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var deviceInfo = await QuerySnmpDeviceAsync(ipAddress, port, community, timeoutMs, ct);
                    
                    if (deviceInfo != null)
                    {
                        result.IsAccessible = true;
                        result.CommunityString = community;
                        result.DeviceInfo = deviceInfo;
                        result.Severity = "high"; // SNMP access = high severity
                        
                        _logger.LogInformation("SNMP accessible on {IP}:{Port} with community '{Community}' - Device: {Device}",
                            ipAddress, port, community, deviceInfo.SysName ?? deviceInfo.SysDescr);
                        
                        break; // Found working community string
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "SNMP query failed for {IP}:{Port} with community '{Community}'",
                        ipAddress, port, community);
                }
            }

            if (!result.IsAccessible)
            {
                _logger.LogDebug("SNMP not accessible on {IP}:{Port} with default community strings", ipAddress, port);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SNMP scan failed for {IP}:{Port}", ipAddress, port);
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Query SNMP device for system information using specified community string
    /// </summary>
    private async Task<SnmpDeviceInfo?> QuerySnmpDeviceAsync(string ipAddress, int port, string community, int timeoutMs, CancellationToken ct)
    {
        try
        {
            // Simple SNMP v2c GET request packet builder
            var sysDescrRequest = BuildSnmpGetRequest(community, Oids.SysDescr);
            var sysNameRequest = BuildSnmpGetRequest(community, Oids.SysName);
            var sysUpTimeRequest = BuildSnmpGetRequest(community, Oids.SysUpTime);
            var sysLocationRequest = BuildSnmpGetRequest(community, Oids.SysLocation);

            using var udpClient = new UdpClient();
            udpClient.Client.ReceiveTimeout = timeoutMs;
            udpClient.Client.SendTimeout = timeoutMs;

            var endpoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);

            // Query sysDescr first to verify SNMP access
            await udpClient.SendAsync(sysDescrRequest, sysDescrRequest.Length, endpoint);
            
            var receiveTask = udpClient.ReceiveAsync();
            var timeoutTask = Task.Delay(timeoutMs, ct);
            
            if (await Task.WhenAny(receiveTask, timeoutTask) == timeoutTask)
            {
                return null; // Timeout
            }

            var response = receiveTask.Result;
            var sysDescr = ParseSnmpResponse(response.Buffer);

            if (string.IsNullOrEmpty(sysDescr))
                return null;

            var deviceInfo = new SnmpDeviceInfo { SysDescr = sysDescr };

            // Query additional OIDs
            try
            {
                await udpClient.SendAsync(sysNameRequest, sysNameRequest.Length, endpoint);
                response = await udpClient.ReceiveAsync();
                deviceInfo.SysName = ParseSnmpResponse(response.Buffer);
            }
            catch { /* Optional field */ }

            try
            {
                await udpClient.SendAsync(sysUpTimeRequest, sysUpTimeRequest.Length, endpoint);
                response = await udpClient.ReceiveAsync();
                deviceInfo.SysUpTime = ParseSnmpResponse(response.Buffer);
            }
            catch { /* Optional field */ }

            try
            {
                await udpClient.SendAsync(sysLocationRequest, sysLocationRequest.Length, endpoint);
                response = await udpClient.ReceiveAsync();
                deviceInfo.SysLocation = ParseSnmpResponse(response.Buffer);
            }
            catch { /* Optional field */ }

            return deviceInfo;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SNMP query failed for {IP}:{Port}", ipAddress, port);
            return null;
        }
    }

    /// <summary>
    /// Build a simple SNMP v2c GET request packet
    /// </summary>
    private byte[] BuildSnmpGetRequest(string community, string oid)
    {
        // This is a simplified SNMP packet builder for basic GET requests
        // For production use, consider using a library like Lextm.SharpSnmpLib
        
        var communityBytes = System.Text.Encoding.ASCII.GetBytes(community);
        var oidBytes = EncodeOid(oid);

        // SNMP v2c GET request packet structure (simplified)
        var packet = new List<byte>();
        
        // Sequence tag + length (will be calculated)
        packet.Add(0x30); // SEQUENCE
        
        var dataStart = packet.Count;
        packet.Add(0x00); // Placeholder for length
        
        // Version (v2c = 1)
        packet.AddRange(new byte[] { 0x02, 0x01, 0x01 }); // INTEGER 1
        
        // Community string
        packet.Add(0x04); // OCTET STRING
        packet.Add((byte)communityBytes.Length);
        packet.AddRange(communityBytes);
        
        // PDU (GET request)
        packet.Add(0xA0); // GET-REQUEST
        var pduStart = packet.Count;
        packet.Add(0x00); // Placeholder for length
        
        // Request ID
        packet.AddRange(new byte[] { 0x02, 0x01, 0x01 }); // INTEGER 1
        
        // Error status
        packet.AddRange(new byte[] { 0x02, 0x01, 0x00 }); // INTEGER 0
        
        // Error index
        packet.AddRange(new byte[] { 0x02, 0x01, 0x00 }); // INTEGER 0
        
        // Variable bindings
        packet.Add(0x30); // SEQUENCE
        var vbStart = packet.Count;
        packet.Add(0x00); // Placeholder for length
        
        // Variable binding
        packet.Add(0x30); // SEQUENCE
        var vbItemStart = packet.Count;
        packet.Add(0x00); // Placeholder for length
        
        // OID
        packet.Add(0x06); // OBJECT IDENTIFIER
        packet.Add((byte)oidBytes.Length);
        packet.AddRange(oidBytes);
        
        // NULL value
        packet.AddRange(new byte[] { 0x05, 0x00 }); // NULL
        
        // Update lengths
        packet[vbItemStart] = (byte)(packet.Count - vbItemStart - 1);
        packet[vbStart] = (byte)(packet.Count - vbStart - 1);
        packet[pduStart] = (byte)(packet.Count - pduStart - 1);
        packet[dataStart] = (byte)(packet.Count - dataStart - 1);
        
        return packet.ToArray();
    }

    /// <summary>
    /// Encode OID string to binary format
    /// </summary>
    private byte[] EncodeOid(string oid)
    {
        var parts = oid.Split('.').Select(int.Parse).ToArray();
        var encoded = new List<byte>();
        
        // First two numbers are encoded as 40*first + second
        if (parts.Length >= 2)
        {
            encoded.Add((byte)(40 * parts[0] + parts[1]));
            
            for (int i = 2; i < parts.Length; i++)
            {
                var value = parts[i];
                if (value < 128)
                {
                    encoded.Add((byte)value);
                }
                else
                {
                    // Encode large values
                    var bytes = new List<byte>();
                    while (value > 0)
                    {
                        bytes.Insert(0, (byte)((value & 0x7F) | (bytes.Count > 0 ? 0x80 : 0)));
                        value >>= 7;
                    }
                    encoded.AddRange(bytes);
                }
            }
        }
        
        return encoded.ToArray();
    }

    /// <summary>
    /// Parse SNMP response to extract string value
    /// </summary>
    private string? ParseSnmpResponse(byte[] response)
    {
        try
        {
            // Simplified parser - looks for OCTET STRING in response
            for (int i = 0; i < response.Length - 2; i++)
            {
                if (response[i] == 0x04) // OCTET STRING tag
                {
                    var length = response[i + 1];
                    if (length < 128 && i + 2 + length <= response.Length)
                    {
                        return System.Text.Encoding.ASCII.GetString(response, i + 2, length);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse SNMP response");
        }
        
        return null;
    }
}

/// <summary>
/// SNMP scan result
/// </summary>
public class SnmpScanResult
{
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool IsAccessible { get; set; }
    public string? CommunityString { get; set; }
    public SnmpDeviceInfo? DeviceInfo { get; set; }
    public string? Severity { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// SNMP device information from standard MIB-II OIDs
/// </summary>
public class SnmpDeviceInfo
{
    public string? SysDescr { get; set; }      // Device description
    public string? SysName { get; set; }       // Device hostname
    public string? SysUpTime { get; set; }     // System uptime
    public string? SysLocation { get; set; }   // Physical location
    public string? SysContact { get; set; }    // Contact information
}
