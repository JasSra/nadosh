using Microsoft.Extensions.Logging;
using Nadosh.Agents.Models;
using System.Diagnostics;

namespace Nadosh.Agents.Agents;

/// <summary>
/// Agent that discovers available pentest tools in the container environment.
/// Probes for Kali/offensive tools and returns structured capability manifests.
/// </summary>
public class GetCapabilitiesToolsAgent
{
    private readonly ILogger<GetCapabilitiesToolsAgent> _logger;
    private readonly SemaphoreSlim _discoveryLock = new(1, 1);
    private List<ToolCapability>? _cachedAvailableTools;

    /// <summary>
    /// Standard Kali Linux tool installation directories for dynamic discovery.
    /// </summary>
    private static readonly string[] KaliToolPaths = new[]
    {
        "/usr/bin",
        "/usr/sbin",
        "/usr/local/bin",
        "/usr/share/metasploit-framework/tools",
        "/usr/share/nmap/scripts"
    };

    // Kali tool catalog aligned with the base image defined in Dockerfile.agents.
    private static readonly IReadOnlyList<KaliToolDefinition> KnownTools = new List<KaliToolDefinition>
    {
        new()
        {
            ToolName = "nmap",
            PackageName = "nmap",
            ToolSuite = "network-reconnaissance",
            Category = ToolCategory.NetworkScanning,
            ExecutableCandidates = new() { "/usr/bin/nmap" },
            ProbeCommands = new() { "nmap" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "port-scan", "service-detection", "os-fingerprint", "script-scanning" },
            CommonFlags = new() { "-sS", "-sV", "-O", "-A", "-p", "--script", "-oX" },
            RequiresElevation = true,
            UsageHint = "Network exploration and security auditing"
        },
        new()
        {
            ToolName = "masscan",
            PackageName = "masscan",
            ToolSuite = "network-reconnaissance",
            Category = ToolCategory.NetworkScanning,
            ExecutableCandidates = new() { "/usr/bin/masscan" },
            ProbeCommands = new() { "masscan" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "fast-port-scan", "internet-scale-scan", "banner-grab" },
            CommonFlags = new() { "-p", "--rate", "--banners", "-oX", "-oJ" },
            RequiresElevation = true,
            UsageHint = "High-speed TCP port scanner for broad coverage"
        },
        new()
        {
            ToolName = "netcat",
            PackageName = "netcat-traditional",
            ToolSuite = "network-reconnaissance",
            Category = ToolCategory.Discovery,
            ExecutableCandidates = new() { "/bin/nc", "/bin/nc.traditional", "/usr/bin/nc", "/usr/bin/nc.traditional" },
            ProbeCommands = new() { "nc", "nc.traditional", "netcat" },
            VersionArguments = new() { new[] { "-h" } },
            Capabilities = new() { "tcp-connect", "udp-probe", "banner-grab", "manual-service-test" },
            CommonFlags = new() { "-v", "-z", "-u", "-w", "-l" },
            UsageHint = "Swiss-army knife for TCP/UDP probing and banner checks"
        },
        new()
        {
            ToolName = "tcpdump",
            PackageName = "tcpdump",
            ToolSuite = "traffic-analysis",
            Category = ToolCategory.Forensics,
            ExecutableCandidates = new() { "/usr/bin/tcpdump" },
            ProbeCommands = new() { "tcpdump" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "packet-capture", "traffic-filter", "pcap-export" },
            CommonFlags = new() { "-i", "-nn", "-s0", "-c", "-w" },
            RequiresElevation = true,
            UsageHint = "Packet capture for validating network traffic and banners"
        },
        new()
        {
            ToolName = "tshark",
            PackageName = "wireshark-common",
            ToolSuite = "traffic-analysis",
            Category = ToolCategory.Forensics,
            ExecutableCandidates = new() { "/usr/bin/tshark" },
            ProbeCommands = new() { "tshark" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "packet-decode", "display-filter", "pcap-analysis", "field-extraction" },
            CommonFlags = new() { "-i", "-r", "-Y", "-T", "-w" },
            UsageHint = "CLI packet analysis for protocol-level inspection"
        },
        new()
        {
            ToolName = "nikto",
            PackageName = "nikto",
            ToolSuite = "web-assessment",
            Category = ToolCategory.WebScanning,
            ExecutableCandidates = new() { "/usr/bin/nikto" },
            ProbeCommands = new() { "nikto" },
            VersionArguments = new() { new[] { "-Version" }, new[] { "-version" } },
            Capabilities = new() { "web-server-scan", "vulnerability-detection", "misconfig-check" },
            CommonFlags = new() { "-h", "-p", "-ssl", "-Format", "-output" },
            UsageHint = "Web server vulnerability scanner"
        },
        new()
        {
            ToolName = "ffuf",
            PackageName = "ffuf",
            ToolSuite = "web-assessment",
            Category = ToolCategory.WebScanning,
            ExecutableCandidates = new() { "/usr/bin/ffuf" },
            ProbeCommands = new() { "ffuf" },
            VersionArguments = new() { new[] { "-V" }, new[] { "-version" } },
            Capabilities = new() { "directory-brute", "vhost-discover", "parameter-fuzz" },
            CommonFlags = new() { "-u", "-w", "-H", "-mc", "-fc", "-of", "-o" },
            UsageHint = "Fast web fuzzer for content discovery"
        },
        new()
        {
            ToolName = "wfuzz",
            PackageName = "wfuzz",
            ToolSuite = "web-assessment",
            Category = ToolCategory.WebScanning,
            ExecutableCandidates = new() { "/usr/bin/wfuzz" },
            ProbeCommands = new() { "wfuzz" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "content-fuzz", "parameter-fuzz", "header-fuzz" },
            CommonFlags = new() { "-u", "-w", "-H", "-d", "--hc", "--sc" },
            UsageHint = "Flexible web fuzzer for parameters, paths, and headers"
        },
        new()
        {
            ToolName = "dirb",
            PackageName = "dirb",
            ToolSuite = "web-assessment",
            Category = ToolCategory.WebScanning,
            ExecutableCandidates = new() { "/usr/bin/dirb" },
            ProbeCommands = new() { "dirb" },
            VersionArguments = new() { new[] { "-h" } },
            Capabilities = new() { "directory-brute", "wordlist-discovery", "content-enumeration" },
            CommonFlags = new() { "-o", "-X", "-H", "-S", "-w" },
            UsageHint = "Classic web content discovery using wordlists"
        },
        new()
        {
            ToolName = "gobuster",
            PackageName = "gobuster",
            ToolSuite = "web-assessment",
            Category = ToolCategory.Enumeration,
            ExecutableCandidates = new() { "/usr/bin/gobuster" },
            ProbeCommands = new() { "gobuster" },
            VersionArguments = new() { new[] { "version" }, new[] { "--version" } },
            Capabilities = new() { "directory-brute", "dns-enum", "vhost-enum" },
            CommonFlags = new() { "dir", "dns", "vhost", "-u", "-w", "-o" },
            UsageHint = "Directory, DNS, and virtual host brute force enumeration"
        },
        new()
        {
            ToolName = "whatweb",
            PackageName = "whatweb",
            ToolSuite = "web-assessment",
            Category = ToolCategory.WebScanning,
            ExecutableCandidates = new() { "/usr/bin/whatweb" },
            ProbeCommands = new() { "whatweb" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "web-tech-detect", "cms-identify", "version-detect" },
            CommonFlags = new() { "-a", "--log-xml", "--log-json" },
            UsageHint = "Identify web technologies, frameworks, and CMS components"
        },
        new()
        {
            ToolName = "curl",
            PackageName = "curl",
            ToolSuite = "web-assessment",
            Category = ToolCategory.Utility,
            ExecutableCandidates = new() { "/usr/bin/curl" },
            ProbeCommands = new() { "curl" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "http-probe", "header-capture", "api-inspection", "tls-debug" },
            CommonFlags = new() { "-I", "-k", "-s", "-H", "-o" },
            UsageHint = "Raw HTTP client for probing endpoints and collecting headers"
        },
        new()
        {
            ToolName = "wget",
            PackageName = "wget",
            ToolSuite = "web-assessment",
            Category = ToolCategory.Utility,
            ExecutableCandidates = new() { "/usr/bin/wget" },
            ProbeCommands = new() { "wget" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "http-fetch", "mirror-content", "header-capture" },
            CommonFlags = new() { "--spider", "-S", "-O", "-q" },
            UsageHint = "CLI retriever for HTTP content, mirrors, and header inspection"
        },
        new()
        {
            ToolName = "sqlmap",
            PackageName = "sqlmap",
            ToolSuite = "vulnerability-validation",
            Category = ToolCategory.Vulnerability,
            ExecutableCandidates = new() { "/usr/bin/sqlmap" },
            ProbeCommands = new() { "sqlmap" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "sql-injection-detection", "database-enum", "request-analysis" },
            CommonFlags = new() { "-u", "--batch", "--dbs", "--tables", "--risk", "--level" },
            UsageHint = "SQL injection assessment and safe request analysis"
        },
        new()
        {
            ToolName = "nuclei",
            PackageName = "nuclei",
            ToolSuite = "vulnerability-validation",
            Category = ToolCategory.Vulnerability,
            ExecutableCandidates = new() { "/usr/bin/nuclei" },
            ProbeCommands = new() { "nuclei" },
            VersionArguments = new() { new[] { "-version" }, new[] { "--version" } },
            Capabilities = new() { "template-scan", "cve-detection", "misconfig-check" },
            CommonFlags = new() { "-u", "-t", "-severity", "-json", "-o" },
            UsageHint = "Template-driven vulnerability scanner for known CVEs and exposures"
        },
        new()
        {
            ToolName = "sslscan",
            PackageName = "sslscan",
            ToolSuite = "tls-analysis",
            Category = ToolCategory.Vulnerability,
            ExecutableCandidates = new() { "/usr/bin/sslscan" },
            ProbeCommands = new() { "sslscan" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "ssl-tls-scan", "cipher-enum", "protocol-test" },
            CommonFlags = new() { "--xml", "--tlsall", "--show-certificate" },
            UsageHint = "SSL/TLS cipher and protocol scanner"
        },
        new()
        {
            ToolName = "testssl.sh",
            PackageName = "testssl.sh",
            ToolSuite = "tls-analysis",
            Category = ToolCategory.Vulnerability,
            ExecutableCandidates = new() { "/usr/bin/testssl", "/usr/bin/testssl.sh" },
            ProbeCommands = new() { "testssl.sh", "testssl" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "tls-audit", "protocol-check", "vulnerability-detect" },
            CommonFlags = new() { "--json", "--severity", "--warnings" },
            UsageHint = "Testing TLS/SSL encryption anywhere on any port"
        },
        new()
        {
            ToolName = "enum4linux",
            PackageName = "enum4linux",
            ToolSuite = "service-enumeration",
            Category = ToolCategory.Enumeration,
            ExecutableCandidates = new() { "/usr/bin/enum4linux" },
            ProbeCommands = new() { "enum4linux" },
            VersionArguments = new() { new[] { "-h" } },
            Capabilities = new() { "smb-enum", "user-enum", "share-enum", "policy-enum" },
            CommonFlags = new() { "-U", "-S", "-G", "-P", "-a" },
            UsageHint = "Windows/Samba enumeration helper for users, shares, and policies"
        },
        new()
        {
            ToolName = "hydra",
            PackageName = "hydra",
            ToolSuite = "password-auditing",
            Category = ToolCategory.PasswordAttack,
            ExecutableCandidates = new() { "/usr/bin/hydra" },
            ProbeCommands = new() { "hydra" },
            VersionArguments = new() { new[] { "-h" } },
            Capabilities = new() { "password-brute", "credential-testing", "protocol-login-audit" },
            CommonFlags = new() { "-l", "-L", "-p", "-P", "-t", "-o" },
            UsageHint = "Network logon password auditor for approved credential testing"
        },
        new()
        {
            ToolName = "jq",
            PackageName = "jq",
            ToolSuite = "utilities",
            Category = ToolCategory.Utility,
            ExecutableCandidates = new() { "/usr/bin/jq" },
            ProbeCommands = new() { "jq" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "json-filter", "output-normalization", "evidence-parsing" },
            CommonFlags = new() { ".", "-r", "-c", "-e" },
            UsageHint = "JSON processor for normalizing tool output and evidence"
        },
        new()
        {
            ToolName = "git",
            PackageName = "git",
            ToolSuite = "utilities",
            Category = ToolCategory.Utility,
            ExecutableCandidates = new() { "/usr/bin/git" },
            ProbeCommands = new() { "git" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "repository-clone", "template-sync", "artifact-versioning" },
            CommonFlags = new() { "clone", "pull", "status" },
            UsageHint = "Source control utility for templates, scripts, and evidence artifacts"
        },
        new()
        {
            ToolName = "python3",
            PackageName = "python3",
            ToolSuite = "python-tooling",
            Category = ToolCategory.Utility,
            ExecutableCandidates = new() { "/usr/bin/python3" },
            ProbeCommands = new() { "python3" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "script-runtime", "custom-parser", "data-transformation" },
            CommonFlags = new() { "-c", "-m", "-V" },
            UsageHint = "Python runtime for custom parsing and lightweight enrichment helpers"
        },
        new()
        {
            ToolName = "pip3",
            PackageName = "python3-pip",
            ToolSuite = "python-tooling",
            Category = ToolCategory.Utility,
            ExecutableCandidates = new() { "/usr/bin/pip3", "/usr/local/bin/pip3" },
            ProbeCommands = new() { "pip3" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "package-install", "environment-inspection", "tooling-bootstrap" },
            CommonFlags = new() { "list", "show", "install" },
            UsageHint = "Python package manager for installed helper tooling inspection"
        },
        
        // Additional Network Reconnaissance Tools
        new()
        {
            ToolName = "dnsenum",
            PackageName = "dnsenum",
            ToolSuite = "network-reconnaissance",
            Category = ToolCategory.Discovery,
            ExecutableCandidates = new() { "/usr/bin/dnsenum" },
            ProbeCommands = new() { "dnsenum" },
            VersionArguments = new() { new[] { "--help" } },
            Capabilities = new() { "dns-enum", "subdomain-discovery", "zone-transfer" },
            CommonFlags = new() { "-f", "--threads", "-o" },
            UsageHint = "DNS enumeration and subdomain discovery"
        },
        new()
        {
            ToolName = "fierce",
            PackageName = "fierce",
            ToolSuite = "network-reconnaissance",
            Category = ToolCategory.Discovery,
            ExecutableCandidates = new() { "/usr/bin/fierce" },
            ProbeCommands = new() { "fierce" },
            VersionArguments = new() { new[] { "--help" } },
            Capabilities = new() { "dns-recon", "subdomain-brute", "ip-discovery" },
            CommonFlags = new() { "--domain", "--subdomains", "--traverse" },
            UsageHint = "DNS reconnaissance and subdomain enumeration"
        },
        new()
        {
            ToolName = "dnsrecon",
            PackageName = "dnsrecon",
            ToolSuite = "network-reconnaissance",
            Category = ToolCategory.Discovery,
            ExecutableCandidates = new() { "/usr/bin/dnsrecon" },
            ProbeCommands = new() { "dnsrecon" },
            VersionArguments = new() { new[] { "--help" } },
            Capabilities = new() { "dns-enum", "zone-walk", "cache-snooping" },
            CommonFlags = new() { "-d", "-t", "-x", "-json" },
            UsageHint = "Advanced DNS enumeration with multiple query types"
        },
        new()
        {
            ToolName = "host",
            PackageName = "bind9-host",
            ToolSuite = "network-reconnaissance",
            Category = ToolCategory.Discovery,
            ExecutableCandidates = new() { "/usr/bin/host" },
            ProbeCommands = new() { "host" },
            VersionArguments = new() { new[] { "-V" } },
            Capabilities = new() { "dns-lookup", "reverse-dns", "mx-query" },
            CommonFlags = new() { "-t", "-a", "-v" },
            UsageHint = "Simple DNS lookup utility"
        },
        new()
        {
            ToolName = "dig",
            PackageName = "bind9-dnsutils",
            ToolSuite = "network-reconnaissance",
            Category = ToolCategory.Discovery,
            ExecutableCandidates = new() { "/usr/bin/dig" },
            ProbeCommands = new() { "dig" },
            VersionArguments = new() { new[] { "-v" } },
            Capabilities = new() { "dns-query", "trace", "batch-lookup" },
            CommonFlags = new() { "+short", "+trace", "ANY", "AXFR" },
            UsageHint = "DNS interrogation with detailed output"
        },
        new()
        {
            ToolName = "whois",
            PackageName = "whois",
            ToolSuite = "network-reconnaissance",
            Category = ToolCategory.Discovery,
            ExecutableCandidates = new() { "/usr/bin/whois" },
            ProbeCommands = new() { "whois" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "domain-lookup", "registrar-info", "contact-discovery" },
            CommonFlags = new() { "-H", "-h" },
            UsageHint = "Domain registration and ownership lookup"
        },
        
        // Web Application Security Tools
        new()
        {
            ToolName = "wpscan",
            PackageName = "wpscan",
            ToolSuite = "web-assessment",
            Category = ToolCategory.WebScanning,
            ExecutableCandidates = new() { "/usr/bin/wpscan" },
            ProbeCommands = new() { "wpscan" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "wordpress-scan", "plugin-enum", "theme-enum", "vulnerability-detect" },
            CommonFlags = new() { "--url", "--enumerate", "--detection-mode", "--format" },
            UsageHint = "WordPress security scanner"
        },
        new()
        {
            ToolName = "wafw00f",
            PackageName = "wafw00f",
            ToolSuite = "web-assessment",
            Category = ToolCategory.WebScanning,
            ExecutableCandidates = new() { "/usr/bin/wafw00f" },
            ProbeCommands = new() { "wafw00f" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "waf-detection", "fingerprint-security-devices" },
            CommonFlags = new() { "-o", "-a" },
            UsageHint = "Web Application Firewall fingerprinting"
        },
        new()
        {
            ToolName = "commix",
            PackageName = "commix",
            ToolSuite = "vulnerability-validation",
            Category = ToolCategory.Vulnerability,
            ExecutableCandidates = new() { "/usr/bin/commix" },
            ProbeCommands = new() { "commix" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "command-injection-detect", "parameter-tampering" },
            CommonFlags = new() { "--url", "--batch", "--level", "--risk" },
            UsageHint = "Command injection detection and exploitation"
        },
        new()
        {
            ToolName = "xsser",
            PackageName = "xsser",
            ToolSuite = "vulnerability-validation",
            Category = ToolCategory.Vulnerability,
            ExecutableCandidates = new() { "/usr/bin/xsser" },
            ProbeCommands = new() { "xsser" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "xss-detection", "automated-fuzzing" },
            CommonFlags = new() { "-u", "--auto", "--Fp" },
            UsageHint = "Cross-Site Scripting vulnerability scanner"
        },
        
        // Enumeration and SMB Tools
        new()
        {
            ToolName = "smbclient",
            PackageName = "smbclient",
            ToolSuite = "service-enumeration",
            Category = ToolCategory.Enumeration,
            ExecutableCandidates = new() { "/usr/bin/smbclient" },
            ProbeCommands = new() { "smbclient" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "smb-access", "share-enum", "file-list" },
            CommonFlags = new() { "-L", "-N", "-U" },
            UsageHint = "SMB/CIFS client for share enumeration"
        },
        new()
        {
            ToolName = "smbmap",
            PackageName = "smbmap",
            ToolSuite = "service-enumeration",
            Category = ToolCategory.Enumeration,
            ExecutableCandidates = new() { "/usr/bin/smbmap" },
            ProbeCommands = new() { "smbmap" },
            VersionArguments = new() { new[] { "--help" } },
            Capabilities = new() { "share-enum", "permission-check", "drive-list" },
            CommonFlags = new() { "-H", "-u", "-p", "-d" },
            UsageHint = "SMB share enumeration with permission mapping"
        },
        new()
        {
            ToolName = "rpcclient",
            PackageName = "rpcclient",
            ToolSuite = "service-enumeration",
            Category = ToolCategory.Enumeration,
            ExecutableCandidates = new() { "/usr/bin/rpcclient" },
            ProbeCommands = new() { "rpcclient" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "rpc-enum", "user-enum", "group-enum" },
            CommonFlags = new() { "-U", "-N", "-c" },
            UsageHint = "MS-RPC client for Windows enumeration"
        },
        new()
        {
            ToolName = "ldapsearch",
            PackageName = "ldap-utils",
            ToolSuite = "service-enumeration",
            Category = ToolCategory.Enumeration,
            ExecutableCandidates = new() { "/usr/bin/ldapsearch" },
            ProbeCommands = new() { "ldapsearch" },
            VersionArguments = new() { new[] { "-VV" } },
            Capabilities = new() { "ldap-query", "user-enum", "schema-dump" },
            CommonFlags = new() { "-x", "-b", "-H", "-D" },
            UsageHint = "LDAP directory enumeration"
        },
        new()
        {
            ToolName = "snmpwalk",
            PackageName = "snmp",
            ToolSuite = "service-enumeration",
            Category = ToolCategory.Enumeration,
            ExecutableCandidates = new() { "/usr/bin/snmpwalk" },
            ProbeCommands = new() { "snmpwalk" },
            VersionArguments = new() { new[] { "-V" } },
            Capabilities = new() { "snmp-enum", "oid-walk", "community-string-test" },
            CommonFlags = new() { "-v", "-c", "-On" },
            UsageHint = "SNMP enumeration and data extraction"
        },
        
        // Password and Credential Tools
        new()
        {
            ToolName = "john",
            PackageName = "john",
            ToolSuite = "password-auditing",
            Category = ToolCategory.PasswordAttack,
            ExecutableCandidates = new() { "/usr/sbin/john", "/usr/bin/john" },
            ProbeCommands = new() { "john" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "password-crack", "hash-crack", "wordlist-attack" },
            CommonFlags = new() { "--wordlist", "--rules", "--format", "--show" },
            UsageHint = "John the Ripper password cracker"
        },
        new()
        {
            ToolName = "hashcat",
            PackageName = "hashcat",
            ToolSuite = "password-auditing",
            Category = ToolCategory.PasswordAttack,
            ExecutableCandidates = new() { "/usr/bin/hashcat" },
            ProbeCommands = new() { "hashcat" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "gpu-crack", "hash-attack", "mask-attack" },
            CommonFlags = new() { "-m", "-a", "-o", "--force" },
            UsageHint = "Advanced GPU-accelerated password recovery"
        },
        new()
        {
            ToolName = "medusa",
            PackageName = "medusa",
            ToolSuite = "password-auditing",
            Category = ToolCategory.PasswordAttack,
            ExecutableCandidates = new() { "/usr/bin/medusa" },
            ProbeCommands = new() { "medusa" },
            VersionArguments = new() { new[] { "-V" } },
            Capabilities = new() { "parallel-brute", "service-login-test" },
            CommonFlags = new() { "-h", "-u", "-p", "-M", "-t" },
            UsageHint = "Fast parallel network login brute-forcer"
        },
        new()
        {
            ToolName = "ncrack",
            PackageName = "ncrack",
            ToolSuite = "password-auditing",
            Category = ToolCategory.PasswordAttack,
            ExecutableCandidates = new() { "/usr/bin/ncrack" },
            ProbeCommands = new() { "ncrack" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "network-auth-crack", "protocol-brute" },
            CommonFlags = new() { "-U", "-P", "-T", "-v" },
            UsageHint = "High-speed network authentication cracker"
        },
        
        // SSL/TLS Tools
        new()
        {
            ToolName = "openssl",
            PackageName = "openssl",
            ToolSuite = "tls-analysis",
            Category = ToolCategory.Vulnerability,
            ExecutableCandidates = new() { "/usr/bin/openssl" },
            ProbeCommands = new() { "openssl" },
            VersionArguments = new() { new[] { "version" } },
            Capabilities = new() { "cert-verify", "cipher-test", "s_client-connect" },
            CommonFlags = new() { "s_client", "x509", "verify", "ciphers" },
            UsageHint = "OpenSSL toolkit for certificate and TLS operations"
        },
        
        // Wireless Tools
        new()
        {
            ToolName = "aircrack-ng",
            PackageName = "aircrack-ng",
            ToolSuite = "wireless",
            Category = ToolCategory.PasswordAttack,
            ExecutableCandidates = new() { "/usr/bin/aircrack-ng" },
            ProbeCommands = new() { "aircrack-ng" },
            VersionArguments = new() { new[] { "--help" } },
            Capabilities = new() { "wpa-crack", "wep-crack", "packet-analysis" },
            CommonFlags = new() { "-w", "-b", "-e" },
            UsageHint = "Wireless WEP/WPA key cracking",
            RequiresElevation = true
        },
        new()
        {
            ToolName = "reaver",
            PackageName = "reaver",
            ToolSuite = "wireless",
            Category = ToolCategory.PasswordAttack,
            ExecutableCandidates = new() { "/usr/bin/reaver" },
            ProbeCommands = new() { "reaver" },
            VersionArguments = new() { new[] { "-h" } },
            Capabilities = new() { "wps-attack", "pin-brute" },
            CommonFlags = new() { "-i", "-b", "-vv" },
            UsageHint = "WPS brute-force attack tool",
            RequiresElevation = true
        },
        
        // Additional Web Tools
        new()
        {
            ToolName = "burpsuite",
            PackageName = "burpsuite",
            ToolSuite = "web-assessment",
            Category = ToolCategory.WebScanning,
            ExecutableCandidates = new() { "/usr/bin/burpsuite" },
            ProbeCommands = new() { "burpsuite" },
            VersionArguments = new() { new[] { "--version" } },
            Capabilities = new() { "proxy-intercept", "scanner", "intruder" },
            CommonFlags = new() { "--project-file", "--config-file" },
            UsageHint = "Web application security testing platform"
        },
        new()
        {
            ToolName = "zaproxy",
            PackageName = "zaproxy",
            ToolSuite = "web-assessment",
            Category = ToolCategory.WebScanning,
            ExecutableCandidates = new() { "/usr/bin/zaproxy", "/usr/share/zaproxy/zap.sh" },
            ProbeCommands = new() { "zaproxy" },
            VersionArguments = new() { new[] { "-version" } },
            Capabilities = new() { "active-scan", "passive-scan", "spider", "fuzzer" },
            CommonFlags = new() { "-daemon", "-port", "-config" },
            UsageHint = "OWASP ZAP web application security scanner"
        },
        
        // Exploitation Frameworks
        new()
        {
            ToolName = "metasploit",
            PackageName = "metasploit-framework",
            ToolSuite = "exploitation",
            Category = ToolCategory.Exploitation,
            ExecutableCandidates = new() { "/usr/bin/msfconsole", "/usr/share/metasploit-framework/msfconsole" },
            ProbeCommands = new() { "msfconsole" },
            VersionArguments = new() { new[] { "-v" } },
            Capabilities = new() { "exploit-framework", "payload-generation", "post-exploitation" },
            CommonFlags = new() { "-q", "-x", "-r" },
            UsageHint = "Metasploit penetration testing framework"
        },
        new()
        {
            ToolName = "searchsploit",
            PackageName = "exploitdb",
            ToolSuite = "exploitation",
            Category = ToolCategory.Vulnerability,
            ExecutableCandidates = new() { "/usr/bin/searchsploit" },
            ProbeCommands = new() { "searchsploit" },
            VersionArguments = new() { new[] { "--help" } },
            Capabilities = new() { "exploit-search", "cve-lookup", "local-db" },
            CommonFlags = new() { "-t", "-w", "-j", "--exclude" },
            UsageHint = "Exploit-DB command-line search tool"
        },
        
        // Forensics and Analysis
        new()
        {
            ToolName = "binwalk",
            PackageName = "binwalk",
            ToolSuite = "forensics",
            Category = ToolCategory.Forensics,
            ExecutableCandidates = new() { "/usr/bin/binwalk" },
            ProbeCommands = new() { "binwalk" },
            VersionArguments = new() { new[] { "--help" } },
            Capabilities = new() { "firmware-analysis", "file-carving", "signature-scan" },
            CommonFlags = new() { "-e", "-M", "-B" },
            UsageHint = "Firmware analysis and extraction tool"
        },
        new()
        {
            ToolName = "foremost",
            PackageName = "foremost",
            ToolSuite = "forensics",
            Category = ToolCategory.Forensics,
            ExecutableCandidates = new() { "/usr/bin/foremost" },
            ProbeCommands = new() { "foremost" },
            VersionArguments = new() { new[] { "-V" } },
            Capabilities = new() { "file-recovery", "data-carving" },
            CommonFlags = new() { "-i", "-o", "-t" },
            UsageHint = "File recovery based on headers and footers"
        },
        new()
        {
            ToolName = "volatility",
            PackageName = "volatility",
            ToolSuite = "forensics",
            Category = ToolCategory.Forensics,
            ExecutableCandidates = new() { "/usr/bin/volatility", "/usr/bin/vol.py" },
            ProbeCommands = new() { "volatility" },
            VersionArguments = new() { new[] { "--info" } },
            Capabilities = new() { "memory-analysis", "process-dump", "artifact-extraction" },
            CommonFlags = new() { "-f", "--profile", "--plugins" },
            UsageHint = "Memory forensics framework"
        }
    };

    public GetCapabilitiesToolsAgent(ILogger<GetCapabilitiesToolsAgent> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns the full tool catalog expected by the Kali base image regardless of runtime availability.
    /// </summary>
    public List<ToolCapability> GetSupportedToolCatalog()
    {
        return KnownTools
            .Select(BuildCatalogCapability)
            .OrderBy(tool => tool.ToolSuite, StringComparer.OrdinalIgnoreCase)
            .ThenBy(tool => tool.ToolName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Returns the full tool catalog grouped by logical Kali suites.
    /// </summary>
    public Dictionary<string, List<ToolCapability>> GetSupportedToolsGroupedBySuite()
    {
        return GroupToolsBySuite(GetSupportedToolCatalog());
    }

    /// <summary>
    /// Discovers all available pentest tools in the container environment.
    /// </summary>
    public async Task<List<ToolCapability>> DiscoverToolsAsync(CancellationToken ct = default)
    {
        if (_cachedAvailableTools is not null)
        {
            return CloneCapabilities(_cachedAvailableTools);
        }

        await _discoveryLock.WaitAsync(ct);

        try
        {
            if (_cachedAvailableTools is not null)
            {
                return CloneCapabilities(_cachedAvailableTools);
            }

            _logger.LogInformation("Discovering available pentest tools in container...");

            var availableTools = new List<ToolCapability>();

            foreach (var knownTool in KnownTools)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var executablePath = ResolveExecutablePath(knownTool);

                    if (executablePath is not null)
                    {
                        var version = await GetToolVersionAsync(executablePath, knownTool.VersionArguments, ct);
                        var capability = BuildCapability(knownTool, executablePath, version);

                        availableTools.Add(capability);
                        _logger.LogInformation("Discovered {Tool} [{Suite}] v{Version} at {Path}", 
                            capability.ToolName,
                            capability.ToolSuite,
                            version ?? "unknown",
                            executablePath);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to probe for tool {Tool}", knownTool.ToolName);
                }
            }

            _cachedAvailableTools = availableTools
                .OrderBy(tool => tool.ToolSuite, StringComparer.OrdinalIgnoreCase)
                .ThenBy(tool => tool.ToolName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation(
                "Tool discovery complete: {Count} tools available across {SuiteCount} suites",
                _cachedAvailableTools.Count,
                _cachedAvailableTools
                    .Select(tool => tool.ToolSuite ?? "uncategorized")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count());

            return CloneCapabilities(_cachedAvailableTools);
        }
        finally
        {
            _discoveryLock.Release();
        }
    }

    /// <summary>
    /// Discovers tools present in the runtime environment and groups them by suite.
    /// </summary>
    public async Task<Dictionary<string, List<ToolCapability>>> DiscoverToolsGroupedBySuiteAsync(
        CancellationToken ct = default)
    {
        var discoveredTools = await DiscoverToolsAsync(ct);
        return GroupToolsBySuite(discoveredTools);
    }

    /// <summary>
    /// Gets tool capabilities filtered by category or phase.
    /// </summary>
    public async Task<List<ToolCapability>> GetToolsForPhaseAsync(
        AssessmentPhase phase, 
        CancellationToken ct = default)
    {
        var allTools = await DiscoverToolsAsync(ct);

        return phase switch
        {
            AssessmentPhase.Recon => allTools
                .Where(t => t.Category == ToolCategory.Discovery || 
                           t.Category == ToolCategory.NetworkScanning ||
                           t.Category == ToolCategory.Forensics)
                .ToList(),
            
            AssessmentPhase.Enumeration => allTools
                .Where(t => t.Category == ToolCategory.Enumeration || 
                           t.Category == ToolCategory.WebScanning ||
                           t.Category == ToolCategory.Utility ||
                           t.Category == ToolCategory.Discovery)
                .ToList(),
            
            AssessmentPhase.Prioritization => allTools
                .Where(t => t.Category == ToolCategory.Vulnerability ||
                           t.Category == ToolCategory.Utility)
                .ToList(),

            AssessmentPhase.Validation => allTools
                .Where(t => t.Category == ToolCategory.Vulnerability ||
                           t.Category == ToolCategory.PasswordAttack ||
                           t.Category == ToolCategory.WebScanning ||
                           t.Category == ToolCategory.Utility)
                .ToList(),

            AssessmentPhase.Reporting => allTools
                .Where(t => t.Category == ToolCategory.Forensics ||
                           t.Category == ToolCategory.Utility)
                .ToList(),
            
            _ => allTools
        };
    }

    private static ToolCapability BuildCatalogCapability(KaliToolDefinition knownTool)
    {
        var defaultExecutable = knownTool.ExecutableCandidates.FirstOrDefault()
            ?? knownTool.ProbeCommands.FirstOrDefault()
            ?? knownTool.ToolName;

        return BuildCapability(knownTool, defaultExecutable, version: null);
    }

    private static ToolCapability BuildCapability(KaliToolDefinition knownTool, string executablePath, string? version)
    {
        return new ToolCapability
        {
            ToolName = knownTool.ToolName,
            ExecutablePath = executablePath,
            Version = version,
            Category = knownTool.Category,
            ToolSuite = knownTool.ToolSuite,
            PackageName = knownTool.PackageName,
            Capabilities = new List<string>(knownTool.Capabilities),
            CommonFlags = new List<string>(knownTool.CommonFlags),
            Aliases = knownTool.ProbeCommands
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(alias => !string.Equals(alias, knownTool.ToolName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            UsageHint = knownTool.UsageHint,
            RequiresElevation = knownTool.RequiresElevation,
            RequiredDependencies = new List<string>(knownTool.RequiredDependencies)
        };
    }

    private static List<ToolCapability> CloneCapabilities(IEnumerable<ToolCapability> capabilities)
    {
        return capabilities
            .Select(CloneCapability)
            .ToList();
    }

    private static ToolCapability CloneCapability(ToolCapability capability)
    {
        return new ToolCapability
        {
            ToolName = capability.ToolName,
            ExecutablePath = capability.ExecutablePath,
            Version = capability.Version,
            Category = capability.Category,
            ToolSuite = capability.ToolSuite,
            PackageName = capability.PackageName,
            Capabilities = new List<string>(capability.Capabilities),
            CommonFlags = new List<string>(capability.CommonFlags),
            Aliases = new List<string>(capability.Aliases),
            UsageHint = capability.UsageHint,
            RequiresElevation = capability.RequiresElevation,
            RequiredDependencies = new List<string>(capability.RequiredDependencies)
        };
    }

    private static Dictionary<string, List<ToolCapability>> GroupToolsBySuite(IEnumerable<ToolCapability> tools)
    {
        return tools
            .GroupBy(
                tool => string.IsNullOrWhiteSpace(tool.ToolSuite) ? "uncategorized" : tool.ToolSuite!,
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(tool => tool.ToolName, StringComparer.OrdinalIgnoreCase)
                    .Select(CloneCapability)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveExecutablePath(KaliToolDefinition knownTool)
    {
        foreach (var candidate in knownTool.ExecutableCandidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (var commandName in knownTool.ProbeCommands.Prepend(knownTool.ToolName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var resolvedPath = ResolveCommandPath(commandName);
            if (!string.IsNullOrWhiteSpace(resolvedPath))
            {
                return resolvedPath;
            }
        }

        return null;
    }

    private static string? ResolveCommandPath(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return null;
        }

        if ((Path.IsPathRooted(commandName) ||
            commandName.Contains(Path.DirectorySeparatorChar) ||
            commandName.Contains(Path.AltDirectorySeparatorChar)) &&
            File.Exists(commandName))
        {
            return commandName;
        }

        if (File.Exists(commandName))
        {
            return Path.GetFullPath(commandName);
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var candidateFileName in GetCandidateFileNames(commandName))
            {
                var candidatePath = Path.Combine(directory, candidateFileName);
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCandidateFileNames(string commandName)
    {
        yield return commandName;

        if (!OperatingSystem.IsWindows() || !string.IsNullOrWhiteSpace(Path.GetExtension(commandName)))
        {
            yield break;
        }

        var pathExtensions = Environment.GetEnvironmentVariable("PATHEXT")
            ?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? new[] { ".exe", ".cmd", ".bat" };

        foreach (var extension in pathExtensions)
        {
            yield return commandName + extension;
        }
    }

    private async Task<string?> GetToolVersionAsync(
        string executablePath,
        IReadOnlyList<string[]> versionArguments,
        CancellationToken ct)
    {
        foreach (var argumentSet in versionArguments)
        {
            try
            {
                var versionLine = await RunProcessAndCaptureFirstLineAsync(executablePath, argumentSet, ct);
                if (!string.IsNullOrWhiteSpace(versionLine))
                {
                    return versionLine;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get version for {ExecutablePath} using args {Arguments}", executablePath, string.Join(' ', argumentSet));
            }
        }

        return null;
    }

    private static async Task<string?> RunProcessAndCaptureFirstLineAsync(
        string executablePath,
        IEnumerable<string> arguments,
        CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = BuildProcessStartInfo(executablePath, arguments)
        };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        var output = await outputTask;
        var error = await errorTask;

        var firstLine = (output + Environment.NewLine + error)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return null;
        }

        return firstLine.Length > 160 ? firstLine[..160] : firstLine;
    }

    /// <summary>
    /// Gets all available tools grouped by category for multi-step discovery.
    /// </summary>
    public async Task<Dictionary<ToolCategory, List<ToolCapability>>> GetToolsByCategoryAsync(CancellationToken ct = default)
    {
        var allTools = await DiscoverToolsAsync(ct);
        return allTools
            .GroupBy(t => t.Category)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// Gets available tools for a specific category.
    /// </summary>
    public async Task<List<ToolCapability>> GetToolsForCategoryAsync(ToolCategory category, CancellationToken ct = default)
    {
        var allTools = await DiscoverToolsAsync(ct);
        return allTools.Where(t => t.Category == category).ToList();
    }

    /// <summary>
    /// Gets a summary of available tool categories with counts.
    /// </summary>
    public async Task<Dictionary<ToolCategory, int>> GetCategoryInventoryAsync(CancellationToken ct = default)
    {
        var allTools = await DiscoverToolsAsync(ct);
        return allTools
            .GroupBy(t => t.Category)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// Dynamically scans Kali Linux standard paths for additional tools not in the known catalog.
    /// </summary>
    public async Task<List<ToolCapability>> DiscoverUnknownToolsAsync(string[] toolPaths, CancellationToken ct = default)
    {
        var discovered = new List<ToolCapability>();
        var knownToolNames = KnownTools.Select(t => t.ToolName.ToLowerInvariant()).ToHashSet();

        foreach (var path in toolPaths)
        {
            if (!Directory.Exists(path))
                continue;

            try
            {
                var executables = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => IsExecutable(f))
                    .Select(f => Path.GetFileName(f))
                    .Where(name => !string.IsNullOrWhiteSpace(name) && !knownToolNames.Contains(name.ToLowerInvariant()))
                    .ToList();

                _logger.LogInformation("Found {Count} unknown executables in {Path}", executables.Count, path);

                foreach (var executable in executables.Take(20)) // Limit to prevent overload
                {
                    try
                    {
                        var fullPath = Path.Combine(path, executable);
                        var version = await GetToolVersionAsync(fullPath, new List<string[]> { new[] { "--version" } }, ct);

                        discovered.Add(new ToolCapability
                        {
                            ToolName = executable,
                            ExecutablePath = fullPath,
                            Version = version,
                            Category = ToolCategory.Utility, // Default to utility for unknown tools
                            Capabilities = new() { "unknown-capability" },
                            CommonFlags = new(),
                            UsageHint = $"Discovered tool: {executable}"
                        });
                    }
                    catch
                    {
                        // Skip tools that fail probing
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan {Path} for unknown tools", path);
            }
        }

        return discovered;
    }

    private static bool IsExecutable(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            return fileInfo.Exists && !fileInfo.Attributes.HasFlag(FileAttributes.Directory);
        }
        catch
        {
            return false;
        }
    }

    private static ProcessStartInfo BuildProcessStartInfo(string executablePath, IEnumerable<string> arguments)
    {
        return new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = string.Join(' ', arguments.Select(EscapeArgument)),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static string EscapeArgument(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return string.Empty;
        }

        return argument.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0
            ? $"\"{argument.Replace("\"", "\\\"")}\""
            : argument;
    }

    private sealed class KaliToolDefinition
    {
        public required string ToolName { get; init; }
        public required string PackageName { get; init; }
        public required string ToolSuite { get; init; }
        public required ToolCategory Category { get; init; }
        public List<string> ExecutableCandidates { get; init; } = new();
        public List<string> ProbeCommands { get; init; } = new();
        public List<string[]> VersionArguments { get; init; } = new();
        public List<string> Capabilities { get; init; } = new();
        public List<string> CommonFlags { get; init; } = new();
        public string? UsageHint { get; init; }
        public bool RequiresElevation { get; init; }
        public List<string> RequiredDependencies { get; init; } = new();
    }
}
