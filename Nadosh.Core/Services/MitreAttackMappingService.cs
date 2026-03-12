using Nadosh.Core.Models;

namespace Nadosh.Core.Services;

/// <summary>
/// Maps exposed services and vulnerabilities to MITRE ATT&CK framework tactics and techniques.
/// Provides context on how attackers could leverage discovered exposures.
/// </summary>
public class MitreAttackMappingService
{
    /// <summary>
    /// Map an exposure to relevant MITRE ATT&CK tactics and techniques
    /// </summary>
    public MitreMapping MapExposureToMitre(CurrentExposure exposure)
    {
        var mapping = new MitreMapping();
        var service = exposure.Classification?.ToLowerInvariant() ?? string.Empty;
        var port = exposure.Port;
        var hasCves = !string.IsNullOrEmpty(exposure.CveIds);

        // Map based on service type and exposure characteristics
        MapServiceToTactics(service, port, hasCves, mapping);

        return mapping;
    }

    private void MapServiceToTactics(string service, int port, bool hasCves, MitreMapping mapping)
    {
        // Initial Access tactics
        if (IsRemoteAccessService(service, port))
        {
            mapping.AddTactic("Initial Access");
            mapping.AddTechnique("T1078", "Valid Accounts"); // SSH, RDP, Telnet
            mapping.AddTechnique("T1133", "External Remote Services");
        }

        if (service.Contains("http") || port == 80 || port == 443 || port == 8080)
        {
            mapping.AddTactic("Initial Access");
            mapping.AddTechnique("T1190", "Exploit Public-Facing Application");
        }

        // Execution tactics
        if (service == "ssh" || service == "telnet" || service == "rdp")
        {
            mapping.AddTactic("Execution");
            mapping.AddTechnique("T1059", "Command and Scripting Interpreter");
        }

        // Persistence tactics
        if (service == "ssh")
        {
            mapping.AddTactic("Persistence");
            mapping.AddTechnique("T1098", "Account Manipulation");
            mapping.AddTechnique("T1136", "Create Account");
        }

        if (service == "rdp" || service == "smb")
        {
            mapping.AddTactic("Persistence");
            mapping.AddTechnique("T1547", "Boot or Logon Autostart Execution");
        }

        // Credential Access tactics
        if (IsDatabaseService(service))
        {
            mapping.AddTactic("Credential Access");
            mapping.AddTechnique("T1003", "OS Credential Dumping");
            mapping.AddTechnique("T1555", "Credentials from Password Stores");
        }

        if (service == "ftp" || service == "telnet" || service == "http")
        {
            mapping.AddTactic("Credential Access");
            mapping.AddTechnique("T1040", "Network Sniffing"); // Unencrypted protocols
        }

        if (service == "ldap" || service == "smb")
        {
            mapping.AddTactic("Credential Access");
            mapping.AddTechnique("T1557", "Adversary-in-the-Middle");
        }

        // Discovery tactics
        if (service == "snmp")
        {
            mapping.AddTactic("Discovery");
            mapping.AddTechnique("T1046", "Network Service Discovery");
            mapping.AddTechnique("T1018", "Remote System Discovery");
        }

        if (IsDatabaseService(service))
        {
            mapping.AddTactic("Discovery");
            mapping.AddTechnique("T1087", "Account Discovery");
            mapping.AddTechnique("T1083", "File and Directory Discovery");
        }

        // Lateral Movement tactics
        if (service == "smb" || port == 445 || port == 139)
        {
            mapping.AddTactic("Lateral Movement");
            mapping.AddTechnique("T1021.002", "SMB/Windows Admin Shares");
        }

        if (service == "ssh")
        {
            mapping.AddTactic("Lateral Movement");
            mapping.AddTechnique("T1021.004", "SSH");
        }

        if (service == "rdp" || port == 3389)
        {
            mapping.AddTactic("Lateral Movement");
            mapping.AddTechnique("T1021.001", "Remote Desktop Protocol");
        }

        // Collection tactics
        if (IsDatabaseService(service))
        {
            mapping.AddTactic("Collection");
            mapping.AddTechnique("T1005", "Data from Local System");
            mapping.AddTechnique("T1213", "Data from Information Repositories");
        }

        // Command and Control tactics
        if (service.Contains("http") || port == 80 || port == 443)
        {
            mapping.AddTactic("Command and Control");
            mapping.AddTechnique("T1071.001", "Web Protocols");
        }

        if (service == "dns" || port == 53)
        {
            mapping.AddTactic("Command and Control");
            mapping.AddTechnique("T1071.004", "DNS");
        }

        // Exfiltration tactics
        if (service == "ftp" || port == 21)
        {
            mapping.AddTactic("Exfiltration");
            mapping.AddTechnique("T1048.003", "Exfiltration Over Unencrypted Non-C2 Protocol");
        }

        if (service.Contains("http"))
        {
            mapping.AddTactic("Exfiltration");
            mapping.AddTechnique("T1041", "Exfiltration Over C2 Channel");
        }

        // Impact tactics
        if (IsDatabaseService(service))
        {
            mapping.AddTactic("Impact");
            mapping.AddTechnique("T1485", "Data Destruction");
            mapping.AddTechnique("T1486", "Data Encrypted for Impact"); // Ransomware
        }

        // CVE exploitation
        if (hasCves)
        {
            mapping.AddTactic("Initial Access");
            mapping.AddTechnique("T1190", "Exploit Public-Facing Application");
            mapping.AddTactic("Privilege Escalation");
            mapping.AddTechnique("T1068", "Exploitation for Privilege Escalation");
        }
    }

    private bool IsRemoteAccessService(string service, int port)
    {
        return service is "ssh" or "telnet" or "rdp" or "vnc" ||
               port is 22 or 23 or 3389 or 5900;
    }

    private bool IsDatabaseService(string service)
    {
        return service is "mysql" or "postgresql" or "mongodb" or "redis" or "elasticsearch" or 
               "cassandra" or "couchdb" or "mssql" or "oracle";
    }
}

/// <summary>
/// MITRE ATT&CK mapping result
/// </summary>
public class MitreMapping
{
    public HashSet<string> Tactics { get; } = new();
    public Dictionary<string, string> Techniques { get; } = new();

    public void AddTactic(string tactic)
    {
        Tactics.Add(tactic);
    }

    public void AddTechnique(string id, string name)
    {
        Techniques.TryAdd(id, name);
    }

    public string GetTacticsString() => string.Join(",", Tactics.OrderBy(t => t));
    public string GetTechniquesString() => string.Join(",", Techniques.Keys.OrderBy(k => k));
}
