using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Nadosh.Core.Models;

namespace Nadosh.Workers.Rules;

public interface IRuleExecutionService
{
    Task<RuleExecutionOutcome> ExecuteAsync(RuleConfig rule, Observation observation, CancellationToken cancellationToken = default);
}

public sealed class RuleExecutionOutcome
{
    public string ResultStatus { get; init; } = "success";
    public double Confidence { get; init; } = 0.9;
    public string Summary { get; init; } = string.Empty;
    public string EvidenceJson { get; init; } = "{}";
    public string[] Tags { get; init; } = Array.Empty<string>();
    public string? Severity { get; init; }
}

public sealed class RuleExecutionService : IRuleExecutionService
{
    private readonly ILogger<RuleExecutionService> _logger;
    private readonly int _connectTimeoutMs;
    private readonly int _readTimeoutMs;

    public RuleExecutionService(ILogger<RuleExecutionService> logger)
    {
        _logger = logger;
        _connectTimeoutMs = int.TryParse(Environment.GetEnvironmentVariable("NADOSH_STAGE2_CONNECT_TIMEOUT_MS"), out var connect)
            ? connect
            : 5000;
        _readTimeoutMs = int.TryParse(Environment.GetEnvironmentVariable("NADOSH_STAGE2_READ_TIMEOUT_MS"), out var read)
            ? read
            : 7000;
    }

    public async Task<RuleExecutionOutcome> ExecuteAsync(RuleConfig rule, Observation observation, CancellationToken cancellationToken = default)
    {
        var request = ParseRequestDefinition(rule.RequestDefinitionJson);
        var matcher = ParseMatcherDefinition(rule.MatcherDefinitionJson);

        try
        {
            return rule.RuleId switch
            {
                "tls-cert-check" => await ExecuteTlsRuleAsync(rule, request, matcher, observation, cancellationToken),
                "http-title-check" => await ExecuteHttpRuleAsync(rule, request, matcher, observation, cancellationToken),
                "ssh-banner-check" => await ExecuteSshRuleAsync(rule, matcher, observation, cancellationToken),
                "rdp-presence-check" => await ExecuteRdpRuleAsync(rule, matcher, observation, cancellationToken),
                _ => ExecuteGenericFallback(rule, matcher, observation)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return BuildFailureOutcome(rule, observation, "timeout", "Rule execution timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rule {RuleId} failed for {TargetIp}:{Port}", rule.RuleId, observation.TargetId, observation.Port);
            return BuildFailureOutcome(rule, observation, "error", ex.Message);
        }
    }

    private async Task<RuleExecutionOutcome> ExecuteTlsRuleAsync(
        RuleConfig rule,
        RuleRequestDefinition request,
        RuleMatcherDefinition matcher,
        Observation observation,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Protocol, "tls", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.Action, "handshake", StringComparison.OrdinalIgnoreCase))
        {
            return BuildFailureOutcome(rule, observation, "config-error", "Unsupported TLS rule configuration.");
        }

        using var tcpClient = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(_connectTimeoutMs);

        await tcpClient.ConnectAsync(observation.TargetId, observation.Port, connectCts.Token);

        using var networkStream = tcpClient.GetStream();
        using var sslStream = new SslStream(networkStream, false, (_, _, _, _) => true);

        using var authCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        authCts.CancelAfter(_readTimeoutMs);

        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = observation.TargetId,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        }, authCts.Token);

        if (sslStream.RemoteCertificate == null)
        {
            return BuildFailureOutcome(rule, observation, "no-data", "TLS handshake succeeded but no certificate was presented.");
        }

        using var certificate = new X509Certificate2(sslStream.RemoteCertificate);
        var sans = ExtractSans(certificate).ToArray();
        var isExpired = certificate.NotAfter < DateTime.UtcNow;
        var isSelfSigned = certificate.Subject == certificate.Issuer;
        var severity = ResolveSeverity(rule.SeverityMappingJson,
            isExpired ? "expired" : null,
            isSelfSigned ? "selfSigned" : null,
            "default");

        var evidence = new Dictionary<string, object?>
        {
            ["subject"] = certificate.Subject,
            ["issuer"] = certificate.Issuer,
            ["expiry"] = certificate.NotAfter,
            ["validFrom"] = certificate.NotBefore,
            ["sha256"] = certificate.GetCertHashString(HashAlgorithmName.SHA256),
            ["san"] = sans,
            ["selfSigned"] = isSelfSigned,
            ["expired"] = isExpired,
            ["daysUntilExpiry"] = (int)(certificate.NotAfter - DateTime.UtcNow).TotalDays,
            ["tlsProtocol"] = sslStream.SslProtocol.ToString()
        };

        var filteredEvidence = FilterEvidence(evidence, matcher.Collect);
        var summary = $"TLS certificate collected for {observation.TargetId}:{observation.Port} (subject: {certificate.Subject}, expires: {certificate.NotAfter:u})";

        return new RuleExecutionOutcome
        {
            ResultStatus = "success",
            Confidence = 0.98,
            Summary = summary,
            EvidenceJson = JsonSerializer.Serialize(filteredEvidence),
            Tags = ["enriched", "tls", isExpired ? "expired-cert" : "valid-cert", isSelfSigned ? "self-signed" : "ca-issued"],
            Severity = severity
        };
    }

    private async Task<RuleExecutionOutcome> ExecuteHttpRuleAsync(
        RuleConfig rule,
        RuleRequestDefinition request,
        RuleMatcherDefinition matcher,
        Observation observation,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Protocol, "http", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return BuildFailureOutcome(rule, observation, "config-error", "Unsupported HTTP rule configuration.");
        }

        using var tcpClient = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(_connectTimeoutMs);

        await tcpClient.ConnectAsync(observation.TargetId, observation.Port, connectCts.Token);

        var path = string.IsNullOrWhiteSpace(request.Path) ? "/" : request.Path;
        var hostHeader = observation.TargetId;
        var probe = Encoding.ASCII.GetBytes($"GET {path} HTTP/1.0\r\nHost: {hostHeader}\r\nUser-Agent: Nadosh-Stage2/1.0\r\nAccept: */*\r\n\r\n");
        var response = await ReadHttpResponseAsync(observation, tcpClient, probe, cancellationToken);

        if (string.IsNullOrWhiteSpace(response))
        {
            return BuildFailureOutcome(rule, observation, "no-data", "No HTTP response body was returned.");
        }

        var statusCode = ParseStatusCode(response);
        var server = ExtractHeader(response, "Server");
        var title = ExtractHtmlTitle(response);
        var severity = ResolveSeverity(rule.SeverityMappingJson, "default");

        var evidence = new Dictionary<string, object?>
        {
            ["statusCode"] = statusCode,
            ["server"] = server,
            ["title"] = title,
            ["responsePreview"] = response.Length > 512 ? response[..512] : response
        };

        var filteredEvidence = FilterEvidence(evidence, matcher.Collect);
        var summary = $"HTTP metadata collected for {observation.TargetId}:{observation.Port} (status: {statusCode?.ToString() ?? "unknown"}, title: {title ?? "unknown"})";

        return new RuleExecutionOutcome
        {
            ResultStatus = "success",
            Confidence = title != null || server != null ? 0.95 : 0.8,
            Summary = summary,
            EvidenceJson = JsonSerializer.Serialize(filteredEvidence),
            Tags = ["enriched", IsTlsHttp(observation) ? "https" : "http", statusCode is >= 200 and < 400 ? "reachable" : "non-2xx"],
            Severity = severity
        };
    }

    private async Task<RuleExecutionOutcome> ExecuteSshRuleAsync(
        RuleConfig rule,
        RuleMatcherDefinition matcher,
        Observation observation,
        CancellationToken cancellationToken)
    {
        string? liveBanner = null;
        string? liveVersion = null;

        try
        {
            using var tcpClient = new TcpClient();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(_connectTimeoutMs);

            await tcpClient.ConnectAsync(observation.TargetId, observation.Port, connectCts.Token);

            using var stream = tcpClient.GetStream();
            stream.ReadTimeout = _readTimeoutMs;

            // Read the SSH server identification string (ends with \r\n or \n)
            // RFC 4253 §4.2 limits SSH identification strings to 255 chars + CR+LF;
            // we allow up to 512 bytes as a defensive upper bound for non-compliant servers.
            const int MaxSshBannerBytes = 512;
            var sb = new System.Text.StringBuilder();
            var buf = new byte[1];
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readCts.CancelAfter(_readTimeoutMs);

            while (true)
            {
                var n = await stream.ReadAsync(buf.AsMemory(0, 1), readCts.Token);
                if (n == 0) break;
                var ch = (char)buf[0];
                if (ch == '\n') break;
                if (ch != '\r') sb.Append(ch);
                if (sb.Length > MaxSshBannerBytes) break;
            }

            liveBanner = sb.ToString().Trim();

            // Parse version: SSH-2.0-OpenSSH_9.6p1 Ubuntu-3ubuntu13.14 → OpenSSH_9.6p1 Ubuntu-3ubuntu13.14
            if (liveBanner.StartsWith("SSH-", StringComparison.OrdinalIgnoreCase))
            {
                var parts = liveBanner.Split('-', 3);
                if (parts.Length >= 3)
                    liveVersion = parts[2];
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Live SSH probe failed for {TargetIp}:{Port}, falling back to observation data.",
                observation.TargetId, observation.Port);
        }

        // Merge live data with observation fallback
        var banner = liveBanner ?? observation.Banner;
        var version = liveVersion ?? observation.ServiceVersion;
        var isLive = liveBanner != null;

        var severity = ResolveSeverity(rule.SeverityMappingJson, "default");
        var evidence = FilterEvidence(new Dictionary<string, object?>
        {
            ["banner"] = banner,
            ["version"] = version,
            ["service"] = observation.ServiceName,
            ["liveProbe"] = isLive
        }, matcher.Collect);

        return new RuleExecutionOutcome
        {
            ResultStatus = "success",
            Confidence = isLive ? 0.97 : (string.IsNullOrWhiteSpace(banner) ? 0.5 : 0.85),
            Summary = isLive
                ? $"SSH banner collected live for {observation.TargetId}:{observation.Port} (version: {version ?? "unknown"})."
                : $"SSH evidence reused from prior observation for {observation.TargetId}:{observation.Port}.",
            EvidenceJson = JsonSerializer.Serialize(evidence),
            Tags = ["enriched", "ssh", isLive ? "live-probe" : "observation-fallback"],
            Severity = severity
        };
    }

    private async Task<RuleExecutionOutcome> ExecuteRdpRuleAsync(
        RuleConfig rule,
        RuleMatcherDefinition matcher,
        Observation observation,
        CancellationToken cancellationToken)
    {
        var isOpen = false;
        string? rdpCookie = null;

        try
        {
            using var tcpClient = new TcpClient();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(_connectTimeoutMs);

            await tcpClient.ConnectAsync(observation.TargetId, observation.Port, connectCts.Token);
            isOpen = true;

            // Send an RDP Connection Request (X.224 TPDU CR) to get a response
            // Standard RDP initial TPDU: 0300 0013 0e e0 0000 0000 00 cookie
            var rdpProbe = new byte[]
            {
                0x03, 0x00, 0x00, 0x13, // TPKT header: version=3, reserved=0, length=19
                0x0e,                   // X.224: TPDU length=14
                0xe0,                   // X.224: CR TPDU type
                0x00, 0x00,             // DST-REF
                0x00, 0x00,             // SRC-REF
                0x00,                   // CLASS
                0x43, 0x6f, 0x6f, 0x6b, 0x69, 0x65, 0x3a, 0x20 // "Cookie: "
            };

            using var stream = tcpClient.GetStream();
            using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            writeCts.CancelAfter(_readTimeoutMs);

            await stream.WriteAsync(rdpProbe, writeCts.Token);

            var responseBuffer = new byte[256];
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readCts.CancelAfter(_readTimeoutMs);

            var bytesRead = await stream.ReadAsync(responseBuffer.AsMemory(0, responseBuffer.Length), readCts.Token);
            if (bytesRead >= 4 && responseBuffer[0] == 0x03 && responseBuffer[1] == 0x00)
            {
                rdpCookie = $"TPKT_CC_len={responseBuffer[2] << 8 | responseBuffer[3]}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Live RDP probe for {TargetIp}:{Port} result: open={IsOpen}",
                observation.TargetId, observation.Port, isOpen);
        }

        // If live probe failed to connect, fall back to observation state
        if (!isOpen)
        {
            isOpen = observation.State == "open";
        }

        var severity = ResolveSeverity(rule.SeverityMappingJson, isOpen ? "present" : "absent", "default");
        var evidence = FilterEvidence(new Dictionary<string, object?>
        {
            ["service"] = observation.ServiceName,
            ["state"] = isOpen ? "open" : "closed",
            ["port"] = observation.Port,
            ["rdpResponse"] = rdpCookie,
            ["liveProbe"] = rdpCookie != null
        }, matcher.Collect);

        return new RuleExecutionOutcome
        {
            ResultStatus = isOpen ? "success" : "no-data",
            Confidence = rdpCookie != null ? 0.98 : (isOpen ? 0.80 : 0.30),
            Summary = rdpCookie != null
                ? $"RDP presence confirmed via live TPKT handshake for {observation.TargetId}:{observation.Port}."
                : isOpen
                    ? $"RDP port open at {observation.TargetId}:{observation.Port} (no TPKT response)."
                    : $"RDP presence could not be confirmed for {observation.TargetId}:{observation.Port}.",
            EvidenceJson = JsonSerializer.Serialize(evidence),
            Tags = ["enriched", "rdp", rdpCookie != null ? "tpkt-response" : isOpen ? "port-open" : "no-response"],
            Severity = severity
        };
    }

    private RuleExecutionOutcome ExecuteGenericFallback(RuleConfig rule, RuleMatcherDefinition matcher, Observation observation)
    {
        var severity = ResolveSeverity(rule.SeverityMappingJson, "default");
        var evidence = FilterEvidence(new Dictionary<string, object?>
        {
            ["service"] = observation.ServiceName,
            ["version"] = observation.ServiceVersion,
            ["banner"] = observation.Banner,
            ["state"] = observation.State
        }, matcher.Collect);

        return new RuleExecutionOutcome
        {
            ResultStatus = "unsupported",
            Confidence = 0.2,
            Summary = $"Rule {rule.RuleId} is registered but does not yet have a dedicated executor path.",
            EvidenceJson = JsonSerializer.Serialize(evidence),
            Tags = ["enriched", "unsupported-rule", rule.ServiceType],
            Severity = severity
        };
    }

    private RuleExecutionOutcome BuildFailureOutcome(RuleConfig rule, Observation observation, string status, string message)
    {
        var severity = ResolveSeverity(rule.SeverityMappingJson, "default");
        return new RuleExecutionOutcome
        {
            ResultStatus = status,
            Confidence = 0.0,
            Summary = $"Rule {rule.RuleId} failed for {observation.TargetId}:{observation.Port}: {message}",
            EvidenceJson = JsonSerializer.Serialize(new { error = message, observation = observation.Id, rule = rule.RuleId }),
            Tags = ["enriched", "failed", rule.ServiceType],
            Severity = severity
        };
    }

    private async Task<string?> ReadHttpResponseAsync(Observation observation, TcpClient tcpClient, byte[] probe, CancellationToken cancellationToken)
    {
        if (IsTlsHttp(observation))
        {
            using var sslStream = new SslStream(tcpClient.GetStream(), false, (_, _, _, _) => true);
            using var authCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            authCts.CancelAfter(_readTimeoutMs);

            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = observation.TargetId,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, authCts.Token);

            await sslStream.WriteAsync(probe, cancellationToken);
            return await ReadStreamAsync(sslStream, cancellationToken);
        }

        var stream = tcpClient.GetStream();
        await stream.WriteAsync(probe, cancellationToken);
        return await ReadStreamAsync(stream, cancellationToken);
    }

    private async Task<string?> ReadStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readCts.CancelAfter(_readTimeoutMs);

        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), readCts.Token);
        if (bytesRead <= 0)
            return null;

        return Encoding.Latin1.GetString(buffer, 0, bytesRead);
    }

    private static bool IsTlsHttp(Observation observation)
        => observation.Port is 443 or 8443 or 9443
           || (observation.ServiceName?.Contains("https", StringComparison.OrdinalIgnoreCase) ?? false);

    private static int? ParseStatusCode(string response)
    {
        if (!response.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
            return null;

        var statusLine = response.Split('\n', 2)[0];
        var parts = statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && int.TryParse(parts[1], out var code) ? code : null;
    }

    private static string? ExtractHeader(string response, string headerName)
    {
        foreach (var line in response.Split('\n'))
        {
            if (line.StartsWith(headerName + ":", StringComparison.OrdinalIgnoreCase))
                return line[(headerName.Length + 1)..].Trim().TrimEnd('\r');
        }

        return null;
    }

    private static string? ExtractHtmlTitle(string response)
    {
        var titleStart = response.IndexOf("<title>", StringComparison.OrdinalIgnoreCase);
        var titleEnd = response.IndexOf("</title>", StringComparison.OrdinalIgnoreCase);
        if (titleStart < 0 || titleEnd <= titleStart)
            return null;

        return response[(titleStart + 7)..titleEnd].Trim();
    }

    private static IEnumerable<string> ExtractSans(X509Certificate2 certificate)
    {
        foreach (var extension in certificate.Extensions)
        {
            if (extension.Oid?.Value != "2.5.29.17")
                continue;

            var sanText = extension.Format(true);
            foreach (var line in sanText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("DNS Name=", StringComparison.OrdinalIgnoreCase))
                    yield return trimmed["DNS Name=".Length..];
                else if (trimmed.StartsWith("IP Address=", StringComparison.OrdinalIgnoreCase))
                    yield return trimmed["IP Address=".Length..];
            }
        }
    }

    private static Dictionary<string, object?> FilterEvidence(Dictionary<string, object?> evidence, IReadOnlyCollection<string> collect)
    {
        if (collect.Count == 0)
            return evidence;

        return evidence
            .Where(kvp => collect.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    private static string? ResolveSeverity(string severityMappingJson, params string?[] candidateKeys)
    {
        if (string.IsNullOrWhiteSpace(severityMappingJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(severityMappingJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var key in candidateKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
            {
                if (document.RootElement.TryGetProperty(key!, out var value))
                    return value.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static RuleRequestDefinition ParseRequestDefinition(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new RuleRequestDefinition();

        try
        {
            return JsonSerializer.Deserialize<RuleRequestDefinition>(json, JsonOptions()) ?? new RuleRequestDefinition();
        }
        catch
        {
            return new RuleRequestDefinition();
        }
    }

    private static RuleMatcherDefinition ParseMatcherDefinition(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new RuleMatcherDefinition();

        try
        {
            return JsonSerializer.Deserialize<RuleMatcherDefinition>(json, JsonOptions()) ?? new RuleMatcherDefinition();
        }
        catch
        {
            return new RuleMatcherDefinition();
        }
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class RuleRequestDefinition
    {
        public string Protocol { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string Method { get; init; } = string.Empty;
        public string Path { get; init; } = "/";
    }

    private sealed class RuleMatcherDefinition
    {
        public string[] Collect { get; init; } = [];
    }
}
