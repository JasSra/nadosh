using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;
using Nadosh.Core.Scanning;
using Nadosh.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Nadosh.Workers;

/// <summary>
/// Tier 1: Banner Grab Worker.
/// Dequeues confirmed-open ports from Tier 0, performs full TCP connects,
/// sends lightweight protocol probes, captures raw banners, and extracts
/// basic service identification (service name, version from banner text).
/// Interesting services are promoted to Tier 2 (FingerprintJob).
/// </summary>
public class BannerGrabWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BannerGrabWorker> _logger;
    private readonly IJobQueue<FingerprintJob> _fingerprintQueue;
    private readonly IServiceIdentifier _serviceIdentifier;

    private readonly int _connectTimeoutMs;
    private readonly int _readTimeoutMs;
    private static readonly Encoding Latin1 = Encoding.Latin1;

    // Protocol probe template (host is substituted at send time)
    private const string HttpProbeTemplate = "GET / HTTP/1.0\r\nHost: {0}\r\nUser-Agent: Nadosh/1.0\r\nAccept: */*\r\n\r\n";

    // Services that send a banner immediately on connect (no probe needed)
    private static readonly HashSet<string> BannerOnConnect = ["ssh", "ftp", "smtp", "pop3", "imap", "mysql", "redis", "mongodb", "vnc"];

    // Services worth promoting to Tier 2 fingerprinting
    private static readonly HashSet<string> PromoteToTier2 =
    [
        "http", "https", "http-alt", "https-alt", "http-proxy",
        "ssh", "ftp", "smtp", "mysql", "postgresql", "mssql",
        "redis", "mongodb", "elasticsearch", "docker", "kubernetes-api",
        "rdp", "vnc", "telnet", "mqtt"
    ];

    public BannerGrabWorker(
        IServiceProvider serviceProvider,
        ILogger<BannerGrabWorker> logger,
        IJobQueue<FingerprintJob> fingerprintQueue,
        IServiceIdentifier serviceIdentifier)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _fingerprintQueue = fingerprintQueue;
        _serviceIdentifier = serviceIdentifier;

        _connectTimeoutMs = int.TryParse(
            Environment.GetEnvironmentVariable("NADOSH_BANNER_CONNECT_TIMEOUT_MS"), out var c) ? c : 5000;
        _readTimeoutMs = int.TryParse(
            Environment.GetEnvironmentVariable("NADOSH_BANNER_READ_TIMEOUT_MS"), out var r) ? r : 5000;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BannerGrabWorker starting (connectTimeout={Connect}ms, readTimeout={Read}ms)...",
            _connectTimeoutMs, _readTimeoutMs);

        using var scope = _serviceProvider.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue<BannerGrabJob>>();

        while (!stoppingToken.IsCancellationRequested)
        {
            var msg = await queue.DequeueAsync(TimeSpan.FromMinutes(5), stoppingToken);
            if (msg == null)
            {
                await Task.Delay(1000, stoppingToken);
                continue;
            }

            try
            {
                await ProcessJobAsync(msg.Payload, scope.ServiceProvider, stoppingToken);
                await queue.AcknowledgeAsync(msg, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing banner grab for {TargetIp}:{Port}",
                    msg.Payload.TargetIp, msg.Payload.Port);
                if (msg.AttemptCount >= 3)
                    await queue.DeadLetterAsync(msg, ex.Message, stoppingToken);
                else
                    await queue.RejectAsync(msg, reenqueue: true, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(BannerGrabJob job, IServiceProvider scopedProvider, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var serviceName = _serviceIdentifier.IdentifyByPort(job.Port) ?? "unknown";

        var obs = new Observation
        {
            TargetId = job.TargetIp,
            ObservedAt = DateTime.UtcNow,
            Port = job.Port,
            Protocol = job.Protocol,
            State = "open",
            ScanRunId = job.BatchId,
            Tier = ScanTier.BannerGrab,
            ServiceName = serviceName
        };

        try
        {
            using var tcpClient = new TcpClient();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(_connectTimeoutMs);

            await tcpClient.ConnectAsync(job.TargetIp, job.Port, connectCts.Token);
            sw.Stop();
            obs.LatencyMs = (int)sw.ElapsedMilliseconds;

            using var stream = tcpClient.GetStream();
            stream.ReadTimeout = _readTimeoutMs;
            stream.WriteTimeout = _readTimeoutMs;

            string? banner = null;

            // For HTTP/HTTPS ports, try an HTTP probe
            if (IsHttpPort(job.Port, serviceName))
            {
                banner = await SendHttpProbeAsync(stream, job.TargetIp, ct);
                ParseHttpBanner(banner, obs);
            }
            // For services that send banners on connect
            else if (BannerOnConnect.Contains(serviceName))
            {
                banner = await ReadBannerAsync(stream, ct);
            }
            else
            {
                // Generic: wait briefly for a banner, if nothing comes, send a CRLF
                banner = await ReadBannerAsync(stream, ct);
                if (string.IsNullOrEmpty(banner))
                {
                    await stream.WriteAsync("\r\n"u8.ToArray(), ct);
                    banner = await ReadBannerAsync(stream, ct);
                }
            }

            obs.Banner = TruncateBanner(banner, 4096);

            // Try to extract version from banner
            ExtractVersionFromBanner(obs);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            obs.Banner = "[timeout]";
        }
        catch (SocketException ex)
        {
            obs.State = "closed";
            obs.Banner = $"[socket-error:{ex.SocketErrorCode}]";
        }
        catch (Exception ex)
        {
            obs.Banner = $"[error:{ex.GetType().Name}]";
        }

        // Persist
        var db = scopedProvider.GetRequiredService<NadoshDbContext>();
        db.Observations.Add(obs);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Banner grab {TargetIp}:{Port} — service={Service}, banner={BannerLen}chars",
            job.TargetIp, job.Port, obs.ServiceName, obs.Banner?.Length ?? 0);

        // Promote to Tier 2 if the service is interesting
        if (obs.State == "open" && PromoteToTier2.Contains(serviceName))
        {
            await _fingerprintQueue.EnqueueAsync(new FingerprintJob
            {
                BatchId = job.BatchId,
                TargetIp = job.TargetIp,
                Port = job.Port,
                ServiceName = obs.ServiceName ?? serviceName,
                BannerObservationId = obs.Id
            }, idempotencyKey: $"fp:{job.TargetIp}:{job.Port}:{job.BatchId}");
        }
    }

    private static bool IsHttpPort(int port, string serviceName)
    {
        return serviceName.Contains("http", StringComparison.OrdinalIgnoreCase) ||
               port is 80 or 443 or 8080 or 8443 or 8000 or 8888 or 8081 or 8082 or 9090 or 9443;
    }

    private async Task<string?> SendHttpProbeAsync(NetworkStream stream, string host, CancellationToken ct)
    {
        var probe = Encoding.ASCII.GetBytes(
            $"GET / HTTP/1.0\r\nHost: {host}\r\nUser-Agent: Nadosh/1.0\r\nAccept: */*\r\n\r\n");

        await stream.WriteAsync(probe, ct);
        return await ReadBannerAsync(stream, ct, maxBytes: 8192);
    }

    private async Task<string?> ReadBannerAsync(NetworkStream stream, CancellationToken ct, int maxBytes = 4096)
    {
        var buffer = new byte[maxBytes];
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readCts.CancelAfter(_readTimeoutMs);

        try
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, maxBytes), readCts.Token);
            if (bytesRead > 0)
            {
                return Latin1.GetString(buffer, 0, bytesRead);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }

        return null;
    }

    private static void ParseHttpBanner(string? banner, Observation obs)
    {
        if (string.IsNullOrEmpty(banner)) return;

        // Extract HTTP status code
        if (banner.StartsWith("HTTP/"))
        {
            var statusLine = banner.Split('\n')[0];
            var parts = statusLine.Split(' ');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var statusCode))
            {
                obs.HttpStatusCode = statusCode;
            }
        }

        // Extract Server header
        var serverMatch = ExtractHeader(banner, "Server");
        if (serverMatch != null)
        {
            obs.HttpServer = serverMatch;
            obs.ServiceVersion = serverMatch;
        }

        // Extract Title from HTML body
        var titleStart = banner.IndexOf("<title>", StringComparison.OrdinalIgnoreCase);
        var titleEnd = banner.IndexOf("</title>", StringComparison.OrdinalIgnoreCase);
        if (titleStart >= 0 && titleEnd > titleStart)
        {
            titleStart += 7; // Length of "<title>"
            obs.HttpTitle = banner[titleStart..titleEnd].Trim();
        }
    }

    private static string? ExtractHeader(string response, string headerName)
    {
        var lines = response.Split('\n');
        foreach (var line in lines)
        {
            if (line.StartsWith(headerName + ":", StringComparison.OrdinalIgnoreCase))
            {
                return line[(headerName.Length + 1)..].Trim().TrimEnd('\r');
            }
        }
        return null;
    }

    private static void ExtractVersionFromBanner(Observation obs)
    {
        if (string.IsNullOrEmpty(obs.Banner)) return;

        var banner = obs.Banner;

        // SSH: "SSH-2.0-OpenSSH_8.9p1 Ubuntu-3ubuntu0.6"
        if (banner.StartsWith("SSH-"))
        {
            obs.ServiceName = "ssh";
            var parts = banner.Split('-', 3);
            if (parts.Length >= 3)
            {
                obs.ServiceVersion = parts[2].Split('\n')[0].Trim();
                if (obs.ServiceVersion.Contains("OpenSSH", StringComparison.OrdinalIgnoreCase))
                    obs.ProductVendor = "OpenBSD";
            }
        }
        // FTP: "220 (vsFTPd 3.0.5)"
        else if (banner.StartsWith("220"))
        {
            obs.ServiceName ??= "ftp";
            var parenStart = banner.IndexOf('(');
            var parenEnd = banner.IndexOf(')');
            if (parenStart >= 0 && parenEnd > parenStart)
            {
                obs.ServiceVersion = banner[(parenStart + 1)..parenEnd];
            }
        }
        // SMTP: "220 mail.example.com ESMTP Postfix"
        else if (banner.Contains("ESMTP") || banner.Contains("SMTP"))
        {
            obs.ServiceName ??= "smtp";
            obs.ServiceVersion = banner.Split('\n')[0].Trim();
        }
        // Redis: "+PONG" or "-ERR" or "$..."
        else if (banner.StartsWith("+") || banner.StartsWith("-ERR") || banner.StartsWith("$"))
        {
            obs.ServiceName ??= "redis";
        }
        // MySQL: banner starts with a byte length then version string
        else if (obs.ServiceName == "mysql" && banner.Length > 5)
        {
            // MySQL protocol: skip first 4 bytes (packet header), version string follows
            var versionEnd = banner.IndexOf('\0', 4);
            if (versionEnd > 4)
            {
                obs.ServiceVersion = banner[4..versionEnd];
            }
        }
    }

    private static string? TruncateBanner(string? banner, int maxLen)
    {
        if (banner == null) return null;
        return banner.Length <= maxLen ? banner : banner[..maxLen];
    }
}
