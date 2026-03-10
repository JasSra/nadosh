using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;

namespace Nadosh.Core.Scanning;

/// <summary>
/// Well-known port-to-service mapper and severity classifier.
/// Provides immediate service identification before banner grabbing occurs.
/// </summary>
public class WellKnownServiceIdentifier : IServiceIdentifier
{
    public string? IdentifyByPort(int port)
    {
        return PortProfiles.PortServiceMap.TryGetValue(port, out var name) ? name : null;
    }

    public string ClassifySeverity(int port, string state, string? serviceName)
    {
        if (state != "open") return "info";

        // High severity: dangerous services exposed to the internet
        if (PortProfiles.HighSeverityPorts.Contains(port))
            return "high";

        // Medium severity: services that shouldn't generally be public
        if (serviceName is "ftp" or "telnet" or "snmp" or "ldap" or "smb" or "netbios-ssn")
            return "medium";

        // Low severity: standard web/email services
        return "low";
    }
}
