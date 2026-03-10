using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Models;
using Nadosh.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Nadosh.Workers;

/// <summary>
/// Tier 2: Deep Fingerprint Worker.
/// Performs protocol-specific probes on bannered services:
///   - HTTP: Full request → extract headers, title, server, status code
///   - TLS/SSL: ClientHello → certificate chain extraction → CertificateObservation
///   - SSH: Parse version banner and key exchange info
///   - Generic: Enhanced banner parsing and version extraction
/// Results are written to Observation (tier=Fingerprint) and CertificateObservation.
/// Also updates CurrentExposure via the existing classifier pipeline.
/// </summary>
public class FingerprintWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FingerprintWorker> _logger;
    private readonly IJobQueue<ClassificationJob> _classificationQueue;

    private readonly int _connectTimeoutMs;
    private readonly int _readTimeoutMs;

    public FingerprintWorker(
        IServiceProvider serviceProvider,
        ILogger<FingerprintWorker> logger,
        IJobQueue<ClassificationJob> classificationQueue)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _classificationQueue = classificationQueue;

        _connectTimeoutMs = int.TryParse(
            Environment.GetEnvironmentVariable("NADOSH_FINGERPRINT_CONNECT_TIMEOUT_MS"), out var c) ? c : 10000;
        _readTimeoutMs = int.TryParse(
            Environment.GetEnvironmentVariable("NADOSH_FINGERPRINT_READ_TIMEOUT_MS"), out var r) ? r : 10000;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FingerprintWorker starting (connectTimeout={Connect}ms, readTimeout={Read}ms)...",
            _connectTimeoutMs, _readTimeoutMs);

        using var scope = _serviceProvider.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue<FingerprintJob>>();

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
                _logger.LogError(ex, "Error processing fingerprint for {TargetIp}:{Port}",
                    msg.Payload.TargetIp, msg.Payload.Port);
                if (msg.AttemptCount >= 3)
                    await queue.DeadLetterAsync(msg, ex.Message, stoppingToken);
                else
                    await queue.RejectAsync(msg, reenqueue: true, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(FingerprintJob job, IServiceProvider scopedProvider, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        _logger.LogInformation("Fingerprinting {TargetIp}:{Port} (service={Service})",
            job.TargetIp, job.Port, job.ServiceName);

        var obs = new Observation
        {
            TargetId = job.TargetIp,
            ObservedAt = DateTime.UtcNow,
            Port = job.Port,
            Protocol = "tcp",
            State = "open",
            ScanRunId = job.BatchId,
            Tier = ScanTier.Fingerprint,
            ServiceName = job.ServiceName
        };

        var db = scopedProvider.GetRequiredService<NadoshDbContext>();

        try
        {
            // Route to the appropriate protocol handler
            if (IsTlsPort(job.Port, job.ServiceName))
            {
                await FingerprintTlsAsync(job, obs, db, ct);
            }
            else if (IsHttpPort(job.Port, job.ServiceName))
            {
                await FingerprintHttpAsync(job, obs, ct);
            }
            else if (job.ServiceName is "ssh" or "ssh-alt")
            {
                await FingerprintSshAsync(job, obs, ct);
            }
            else if (job.ServiceName is "ftp")
            {
                await FingerprintFtpAsync(job, obs, ct);
            }
            else
            {
                await FingerprintGenericAsync(job, obs, ct);
            }

            sw.Stop();
            obs.LatencyMs = (int)sw.ElapsedMilliseconds;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            obs.Banner = "[timeout]";
        }
        catch (Exception ex)
        {
            obs.Banner = $"[error:{ex.GetType().Name}:{ex.Message}]";
            _logger.LogWarning(ex, "Fingerprint failed for {TargetIp}:{Port}", job.TargetIp, job.Port);
        }

        // Persist observation
        db.Observations.Add(obs);
        await db.SaveChangesAsync(ct);

        // Feed into classification pipeline
        await _classificationQueue.EnqueueAsync(
            new ClassificationJob { Observation = obs },
            idempotencyKey: $"clf-fp:{obs.Id}");

        _logger.LogInformation("Fingerprint done: {TargetIp}:{Port} — service={Service}, version={Version}",
            job.TargetIp, job.Port, obs.ServiceName, obs.ServiceVersion ?? "unknown");
    }

    // ─── TLS Fingerprinting ──────────────────────────────────────────────────────

    private async Task FingerprintTlsAsync(FingerprintJob job, Observation obs, NadoshDbContext db, CancellationToken ct)
    {
        using var tcpClient = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(_connectTimeoutMs);

        await tcpClient.ConnectAsync(job.TargetIp, job.Port, connectCts.Token);

        using var networkStream = tcpClient.GetStream();
        using var sslStream = new SslStream(networkStream, false, (sender, cert, chain, errors) => true);

        var sslOptions = new SslClientAuthenticationOptions
        {
            TargetHost = job.TargetIp,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        };

        using var sslCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        sslCts.CancelAfter(_readTimeoutMs);

        await sslStream.AuthenticateAsClientAsync(sslOptions, sslCts.Token);

        // Extract certificate info
        var remoteCert = sslStream.RemoteCertificate;
        if (remoteCert != null)
        {
            using var x509 = new X509Certificate2(remoteCert);

            obs.SslSubject = x509.Subject;
            obs.SslIssuer = x509.Issuer;
            obs.SslExpiry = x509.NotAfter;
            obs.ServiceVersion = $"TLS {sslStream.SslProtocol}";

            // Persist full certificate observation
            var certObs = new CertificateObservation
            {
                TargetId = job.TargetIp,
                Port = job.Port,
                ObservedAt = DateTime.UtcNow,
                Subject = x509.Subject,
                Issuer = x509.Issuer,
                ValidFrom = x509.NotBefore,
                ValidTo = x509.NotAfter,
                SerialNumber = x509.SerialNumber,
                Sha256 = x509.GetCertHashString(HashAlgorithmName.SHA256),
                SignatureAlgorithm = x509.SignatureAlgorithm.FriendlyName ?? "unknown",
                KeySize = x509.GetRSAPublicKey()?.KeySize ?? x509.GetECDsaPublicKey()?.KeySize,
                SanList = ExtractSans(x509).ToList(),
                IsExpired = x509.NotAfter < DateTime.UtcNow,
                DaysUntilExpiry = (int)(x509.NotAfter - DateTime.UtcNow).TotalDays,
                IsSelfSigned = x509.Subject == x509.Issuer
            };

            db.CertificateObservations.Add(certObs);
        }

        // Also try HTTP over TLS to get server info
        try
        {
            var probe = Encoding.ASCII.GetBytes(
                $"GET / HTTP/1.0\r\nHost: {job.TargetIp}\r\nUser-Agent: Nadosh/1.0\r\n\r\n");
            await sslStream.WriteAsync(probe, ct);

            var buffer = new byte[8192];
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(_readTimeoutMs);

            var bytesRead = await sslStream.ReadAsync(buffer.AsMemory(0, 8192), readCts.Token);
            if (bytesRead > 0)
            {
                var response = Encoding.Latin1.GetString(buffer, 0, bytesRead);
                obs.Banner = response.Length > 4096 ? response[..4096] : response;
                ParseHttpResponse(response, obs);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HTTP-over-TLS probe failed for {TargetIp}:{Port}", job.TargetIp, job.Port);
        }
    }

    // ─── HTTP Fingerprinting ─────────────────────────────────────────────────────

    private async Task FingerprintHttpAsync(FingerprintJob job, Observation obs, CancellationToken ct)
    {
        using var tcpClient = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(_connectTimeoutMs);

        await tcpClient.ConnectAsync(job.TargetIp, job.Port, connectCts.Token);

        using var stream = tcpClient.GetStream();
        stream.ReadTimeout = _readTimeoutMs;

        var probe = Encoding.ASCII.GetBytes(
            $"GET / HTTP/1.0\r\nHost: {job.TargetIp}\r\nUser-Agent: Nadosh/1.0\r\nAccept: */*\r\n\r\n");

        await stream.WriteAsync(probe, ct);

        var buffer = new byte[8192];
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readCts.CancelAfter(_readTimeoutMs);

        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, 8192), readCts.Token);
        if (bytesRead > 0)
        {
            var response = Encoding.Latin1.GetString(buffer, 0, bytesRead);
            obs.Banner = response.Length > 4096 ? response[..4096] : response;
            ParseHttpResponse(response, obs);
        }
    }

    // ─── SSH Fingerprinting ──────────────────────────────────────────────────────

    private async Task FingerprintSshAsync(FingerprintJob job, Observation obs, CancellationToken ct)
    {
        using var tcpClient = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(_connectTimeoutMs);

        await tcpClient.ConnectAsync(job.TargetIp, job.Port, connectCts.Token);

        using var stream = tcpClient.GetStream();
        var buffer = new byte[1024];

        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readCts.CancelAfter(_readTimeoutMs);

        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, 1024), readCts.Token);
        if (bytesRead > 0)
        {
            var banner = Encoding.Latin1.GetString(buffer, 0, bytesRead);
            obs.Banner = banner.Trim();

            // Parse SSH-2.0-OpenSSH_8.9p1 Ubuntu-3ubuntu0.6
            if (banner.StartsWith("SSH-"))
            {
                var parts = banner.Split('-', 3);
                if (parts.Length >= 3)
                {
                    obs.ServiceVersion = parts[2].Split('\n')[0].Trim();
                    if (obs.ServiceVersion.Contains("OpenSSH", StringComparison.OrdinalIgnoreCase))
                        obs.ProductVendor = "OpenBSD";
                    else if (obs.ServiceVersion.Contains("dropbear", StringComparison.OrdinalIgnoreCase))
                        obs.ProductVendor = "Dropbear";
                }
            }
        }
    }

    // ─── FTP Fingerprinting ──────────────────────────────────────────────────────

    private async Task FingerprintFtpAsync(FingerprintJob job, Observation obs, CancellationToken ct)
    {
        using var tcpClient = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(_connectTimeoutMs);

        await tcpClient.ConnectAsync(job.TargetIp, job.Port, connectCts.Token);

        using var stream = tcpClient.GetStream();
        var buffer = new byte[2048];

        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readCts.CancelAfter(_readTimeoutMs);

        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, 2048), readCts.Token);
        if (bytesRead > 0)
        {
            var banner = Encoding.Latin1.GetString(buffer, 0, bytesRead);
            obs.Banner = banner.Trim();

            // Parse "220 (vsFTPd 3.0.5)" or "220-FileZilla Server 1.8.0"
            if (banner.StartsWith("220"))
            {
                // Try parenthesised version
                var parenStart = banner.IndexOf('(');
                var parenEnd = banner.IndexOf(')');
                if (parenStart >= 0 && parenEnd > parenStart)
                {
                    obs.ServiceVersion = banner[(parenStart + 1)..parenEnd];
                    if (obs.ServiceVersion.Contains("vsFTPd", StringComparison.OrdinalIgnoreCase))
                        obs.ProductVendor = "vsFTPd";
                }
                else
                {
                    // "220-FileZilla Server 1.8.0" or "220 ProFTPD 1.3.8"
                    var restOfLine = banner.Length > 4 ? banner[4..].Split('\n')[0].Trim() : "";
                    obs.ServiceVersion = restOfLine;

                    if (restOfLine.Contains("FileZilla", StringComparison.OrdinalIgnoreCase))
                        obs.ProductVendor = "FileZilla";
                    else if (restOfLine.Contains("ProFTPD", StringComparison.OrdinalIgnoreCase))
                        obs.ProductVendor = "ProFTPD";
                    else if (restOfLine.Contains("Pure-FTPd", StringComparison.OrdinalIgnoreCase))
                        obs.ProductVendor = "Pure-FTPd";
                }
            }
        }
    }

    // ─── Generic Fingerprinting ──────────────────────────────────────────────────

    private async Task FingerprintGenericAsync(FingerprintJob job, Observation obs, CancellationToken ct)
    {
        using var tcpClient = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(_connectTimeoutMs);

        await tcpClient.ConnectAsync(job.TargetIp, job.Port, connectCts.Token);

        using var stream = tcpClient.GetStream();
        var buffer = new byte[4096];

        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readCts.CancelAfter(_readTimeoutMs);

        // Wait for initial banner
        try
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, 4096), readCts.Token);
            if (bytesRead > 0)
            {
                obs.Banner = Encoding.Latin1.GetString(buffer, 0, bytesRead);
            }
        }
        catch (OperationCanceledException)
        {
            // No banner sent on connect — try a nudge
            await stream.WriteAsync("\r\n"u8.ToArray(), ct);

            using var nudgeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            nudgeCts.CancelAfter(3000);

            try
            {
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, 4096), nudgeCts.Token);
                if (bytesRead > 0)
                {
                    obs.Banner = Encoding.Latin1.GetString(buffer, 0, bytesRead);
                }
            }
            catch { /* No banner available */ }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static void ParseHttpResponse(string response, Observation obs)
    {
        if (string.IsNullOrEmpty(response)) return;

        // Status code
        if (response.StartsWith("HTTP/"))
        {
            var statusLine = response.Split('\n')[0];
            var parts = statusLine.Split(' ');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var code))
                obs.HttpStatusCode = code;
        }

        // Server header
        var server = ExtractHeader(response, "Server");
        if (server != null)
        {
            obs.HttpServer = server;
            obs.ServiceVersion ??= server;
        }

        // X-Powered-By
        var poweredBy = ExtractHeader(response, "X-Powered-By");
        if (poweredBy != null)
        {
            obs.ProductVendor = poweredBy;
        }

        // Title
        var titleStart = response.IndexOf("<title>", StringComparison.OrdinalIgnoreCase);
        var titleEnd = response.IndexOf("</title>", StringComparison.OrdinalIgnoreCase);
        if (titleStart >= 0 && titleEnd > titleStart)
        {
            obs.HttpTitle = response[(titleStart + 7)..titleEnd].Trim();
        }
    }

    private static string? ExtractHeader(string response, string name)
    {
        var lines = response.Split('\n');
        foreach (var line in lines)
        {
            if (line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
                return line[(name.Length + 1)..].Trim().TrimEnd('\r');
        }
        return null;
    }

    private static string[] ExtractSans(X509Certificate2 cert)
    {
        var sans = new List<string>();
        foreach (var ext in cert.Extensions)
        {
            if (ext.Oid?.Value == "2.5.29.17") // Subject Alternative Name OID
            {
                var sanText = ext.Format(true);
                foreach (var line in sanText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("DNS Name=", StringComparison.OrdinalIgnoreCase))
                        sans.Add(trimmed["DNS Name=".Length..]);
                    else if (trimmed.StartsWith("IP Address=", StringComparison.OrdinalIgnoreCase))
                        sans.Add(trimmed["IP Address=".Length..]);
                }
            }
        }
        return sans.ToArray();
    }

    private static bool IsTlsPort(int port, string serviceName)
    {
        return port is 443 or 8443 or 9443 or 993 or 995 or 465 or 636 or 990 or 5986 or 2376 or 8883 ||
               serviceName.Contains("https", StringComparison.OrdinalIgnoreCase) ||
               serviceName.Contains("tls", StringComparison.OrdinalIgnoreCase) ||
               serviceName.Contains("ssl", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHttpPort(int port, string serviceName)
    {
        return serviceName.Contains("http", StringComparison.OrdinalIgnoreCase) ||
               port is 80 or 8080 or 8000 or 8888 or 8081 or 8082 or 9090;
    }
}
