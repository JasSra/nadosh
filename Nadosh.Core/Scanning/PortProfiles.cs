namespace Nadosh.Core.Scanning;

/// <summary>
/// Curated port lists modelled after Shodan's scanning targets.
/// These cover ~95% of internet-facing services without brute-forcing all 65,535 ports.
/// Organised by tier for progressive scanning depth.
/// </summary>
public static class PortProfiles
{
    /// <summary>
    /// The absolute minimum — 12 ports that catch the vast majority of internet services.
    /// Used for cold/initial sweep of unknown IPs.
    /// </summary>
    public static readonly int[] QuickSweep =
    [
        21,    // FTP
        22,    // SSH
        23,    // Telnet
        25,    // SMTP
        53,    // DNS
        80,    // HTTP
        110,   // POP3
        143,   // IMAP
        443,   // HTTPS
        993,   // IMAPS
        3389,  // RDP
        8080   // HTTP-Alt
    ];

    /// <summary>
    /// Top 100 ports — covers virtually all common internet-facing services.
    /// This is the primary discovery list, comparable to Shodan's default scan set.
    /// </summary>
    public static readonly int[] Top100 =
    [
        // Web
        80, 443, 8080, 8443, 8000, 8888, 8081, 8082, 9090, 9443,
        // Remote access
        22, 23, 3389, 5900, 5901, 5985, 5986,
        // Email
        25, 110, 143, 465, 587, 993, 995,
        // DNS
        53,
        // File transfer
        21, 69, 990,
        // Database
        1433, 1521, 3306, 5432, 6379, 9200, 9300, 27017, 27018,
        // Message queues
        5672, 15672, 61616, 9092,
        // LDAP / Directory
        389, 636,
        // Monitoring / Management
        161, 162, 199, 8291, 10000, 10443,
        // VPN / Tunneling
        500, 1194, 1723, 4500,
        // Proxy / Load balancer
        3128, 8118, 1080,
        // IoT / Industrial
        502, 1883, 8883, 47808, 44818, 20000, 2404,
        // Media / Streaming
        554, 1935, 8554,
        // Windows services
        135, 137, 138, 139, 445, 593,
        // Misc common
        111, 2049, 2222, 4040, 4443, 4848, 5000, 5001, 6000, 7001, 7002,
        7443, 8009, 8090, 8180, 8880, 9000, 9001, 11211, 50000,
        // Kubernetes / Container
        2375, 2376, 6443, 10250, 10255,
        // CI/CD
        8161, 8500, 8600, 9090
    ];

    /// <summary>
    /// Extended list — 200+ ports for warm/hot targets that already show activity.
    /// Includes more exotic services, admin panels, and backdoor ports.
    /// </summary>
    public static readonly int[] Extended =
    [
        .. Top100,
        // Additional web servers / admin panels
        81, 82, 83, 84, 85, 88, 280, 591, 2082, 2083, 2086, 2087,
        2095, 2096, 4567, 5104, 5601, 6080, 6443, 7070, 7080, 7443,
        7548, 8001, 8002, 8008, 8010, 8014, 8042, 8069, 8088, 8091,
        8095, 8123, 8172, 8222, 8280, 8281, 8333, 8383, 8444, 8500,
        8834, 8880, 8983, 9091, 9200, 9443, 9944, 9981, 12443, 18080,
        // More databases
        5984, 7474, 8086, 8529, 28015, 28017, 29015,
        // Additional IoT / SCADA
        102, 789, 1911, 2000, 4000, 4911, 9100, 9160, 18245, 34962, 34963, 34964
    ];

    /// <summary>
    /// Maps well-known ports to their expected service names.
    /// Used by the service identifier for initial classification before banner grabbing.
    /// </summary>
    public static readonly Dictionary<int, string> PortServiceMap = new()
    {
        [21] = "ftp", [22] = "ssh", [23] = "telnet", [25] = "smtp",
        [53] = "dns", [69] = "tftp", [80] = "http", [81] = "http",
        [88] = "kerberos", [110] = "pop3", [111] = "rpcbind", [135] = "msrpc",
        [137] = "netbios-ns", [138] = "netbios-dgm", [139] = "netbios-ssn",
        [143] = "imap", [161] = "snmp", [162] = "snmptrap", [199] = "smux",
        [389] = "ldap", [443] = "https", [445] = "microsoft-ds",
        [465] = "smtps", [500] = "isakmp", [502] = "modbus",
        [554] = "rtsp", [587] = "submission", [593] = "http-rpc-epmap",
        [636] = "ldaps", [993] = "imaps", [995] = "pop3s",
        [1080] = "socks", [1194] = "openvpn", [1433] = "mssql",
        [1521] = "oracle", [1723] = "pptp", [1883] = "mqtt",
        [1935] = "rtmp", [2049] = "nfs", [2222] = "ssh-alt",
        [2375] = "docker", [2376] = "docker-tls", [3128] = "squid-proxy",
        [3306] = "mysql", [3389] = "rdp", [4443] = "https-alt",
        [4500] = "nat-t-ike", [5000] = "upnp", [5432] = "postgresql",
        [5672] = "amqp", [5900] = "vnc", [5901] = "vnc",
        [5984] = "couchdb", [5985] = "winrm-http", [5986] = "winrm-https",
        [6379] = "redis", [6443] = "kubernetes-api", [7001] = "weblogic",
        [8000] = "http-alt", [8080] = "http-proxy", [8081] = "http-alt",
        [8443] = "https-alt", [8500] = "consul", [8834] = "nessus",
        [8883] = "mqtt-tls", [8888] = "http-alt", [9000] = "sonarqube",
        [9090] = "prometheus", [9092] = "kafka", [9200] = "elasticsearch",
        [9300] = "elasticsearch-cluster", [10000] = "webmin",
        [10250] = "kubelet", [10255] = "kubelet-ro",
        [11211] = "memcached", [15672] = "rabbitmq-mgmt",
        [27017] = "mongodb", [27018] = "mongodb",
        [50000] = "sap", [61616] = "activemq"
    };

    /// <summary>
    /// Ports that indicate high-severity exposure when found open on the public internet.
    /// </summary>
    public static readonly HashSet<int> HighSeverityPorts =
    [
        22, 23, 135, 137, 139, 445, 502, 1433, 1521, 2375,
        3306, 3389, 5432, 5900, 5901, 6379, 9200, 10250,
        11211, 27017
    ];
}
