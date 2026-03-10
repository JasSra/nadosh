# Populate enrichment data for discovered open ports
# This bypasses the worker queue and directly inserts enriched observations

$connString = "Host=localhost;Port=5439;Database=nadosh;Username=nadosh;Password=nadosh_password"

# Define open ports from discovery scan
$openPorts = @(
    @{IP="192.168.4.25"; Port=22},
    @{IP="192.168.4.25"; Port=80},
    @{IP="192.168.4.25"; Port=443},
    @{IP="192.168.4.27"; Port=443},
    @{IP="192.168.4.98"; Port=22},
    @{IP="192.168.4.101"; Port=80},
    @{IP="192.168.4.102"; Port=22},
    @{IP="192.168.4.102"; Port=80},
    @{IP="192.168.4.102"; Port=443},
    @{IP="192.168.4.116"; Port=80},
    @{IP="192.168.4.136"; Port=22},
    @{IP="192.168.4.136"; Port=80},
    @{IP="192.168.4.136"; Port=443},
    @{IP="192.168.4.151"; Port=80},
    @{IP="192.168.4.151"; Port=443},
    @{IP="192.168.4.216"; Port=22},
    @{IP="192.168.4.219"; Port=22}
)

$enrichedData = @()

Write-Host "=== Gathering Enrichment Data ===" -ForegroundColor Cyan

foreach ($ep in $openPorts) {
    $ip = $ep.IP
    $port = $ep.Port
    
    $data = @{
        IP = $ip
        Port = $port
        Banner = $null
        ServiceName = $null
        HttpTitle = $null
        HttpServer = $null
        HttpStatusCode = $null
        SslSubject = $null
        SslIssuer = $null
        SslExpiry = $null
    }
    
    try {
        if ($port -eq 22) {
            # SSH Banner Grab
            $client = New-Object System.Net.Sockets.TcpClient
            $client.ReceiveTimeout = 3000
            $client.SendTimeout = 3000
            $client.Connect($ip, $port)
            $stream = $client.GetStream()
            $buffer = New-Object byte[] 1024
            $stream.ReadTimeout = 3000
            $bytesRead = $stream.Read($buffer, 0, 1024)
            $banner = [System.Text.Encoding]::ASCII.GetString($buffer, 0, $bytesRead).Trim()
            $data.Banner = $banner
            $data.ServiceName = "ssh"
            Write-Host "✓ $ip`:$port SSH: $banner" -ForegroundColor Green
            $client.Close()
        }
        elseif ($port -eq 80) {
            # HTTP Probe
            try {
                $resp = Invoke-WebRequest -Uri "http://$ip`:$port/" -TimeoutSec 5 -UseBasicParsing -MaximumRedirection 0 -ErrorAction Stop
                $data.HttpStatusCode = $resp.StatusCode
                $data.HttpServer = $resp.Headers["Server"]
                if ($resp.Content -match "<title>(.*?)</title>") {
                    $data.HttpTitle = $Matches[1]
                }
                $data.ServiceName = "http"
                Write-Host "✓ $ip`:$port HTTP $($resp.StatusCode) | Server: $($data.HttpServer) | Title: $($data.HttpTitle)" -ForegroundColor Green
            } catch {
                $data.HttpStatusCode = 0
                $data.ServiceName = "http"
                Write-Host "⚠ $ip`:$port HTTP Error: $($_.Exception.Message.Substring(0, [Math]::Min(80, $_.Exception.Message.Length)))" -ForegroundColor Yellow
            }
        }
        elseif ($port -eq 443) {
            # HTTPS + TLS Cert
            try {
                $resp = Invoke-WebRequest -Uri "https://$ip`:$port/" -TimeoutSec 5 -UseBasicParsing -SkipCertificateCheck -MaximumRedirection 0 -ErrorAction Stop
                $data.HttpStatusCode = $resp.StatusCode
                $data.HttpServer = $resp.Headers["Server"]
                if ($resp.Content -match "<title>(.*?)</title>") {
                    $data.HttpTitle = $Matches[1]
                }
                $data.ServiceName = "https"
                Write-Host "✓ $ip`:$port HTTPS $($resp.StatusCode) | Server: $($data.HttpServer) | Title: $($data.HttpTitle)" -ForegroundColor Green
            } catch {
                $data.HttpStatusCode = 0
                $data.ServiceName = "https"
            }
            
            # Get TLS cert
            try {
                $tcp = New-Object System.Net.Sockets.TcpClient($ip, [int]$port)
                $ssl = New-Object System.Net.Security.SslStream($tcp.GetStream(), $false, { $true })
                $ssl.AuthenticateAsClient($ip)
                $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($ssl.RemoteCertificate)
                $data.SslSubject = $cert.Subject
                $data.SslIssuer = $cert.Issuer
                $data.SslExpiry = $cert.NotAfter.ToString("yyyy-MM-dd HH:mm:ss")
                Write-Host "  └─ TLS: $($cert.Subject) (expires $($cert.NotAfter.ToString('yyyy-MM-dd')))" -ForegroundColor Gray
                $ssl.Close()
                $tcp.Close()
            } catch {
                Write-Host "  └─ TLS cert error: $($_.Exception.Message.Substring(0, [Math]::Min(60, $_.Exception.Message.Length)))" -ForegroundColor DarkYellow
            }
        }
    } catch {
        Write-Host "✗ $ip`:$port Error: $($_.Exception.Message)" -ForegroundColor Red
    }
    
    $enrichedData += $data
}

Write-Host "`n=== Updating Database ===" -ForegroundColor Cyan

# Update observations with enrichment data using psql
foreach ($data in $enrichedData) {
    $banner = if ($data.Banner) { $data.Banner.Replace("'", "''") } else { $null }
    $serviceName = if ($data.ServiceName) { "'$($data.ServiceName)'" } else { "NULL" }
    $httpTitle = if ($data.HttpTitle) { "'$($data.HttpTitle.Replace("'", "''"))'" } else { "NULL" }
    $httpServer = if ($data.HttpServer) { "'$($data.HttpServer.Replace("'", "''"))'" } else { "NULL" }
    $httpStatusCode = if ($data.HttpStatusCode) { $data.HttpStatusCode } else { "NULL" }
    $sslSubject = if ($data.SslSubject) { "'$($data.SslSubject.Replace("'", "''"))'" } else { "NULL" }
    $sslIssuer = if ($data.SslIssuer) { "'$($data.SslIssuer.Replace("'", "''"))'" } else { "NULL" }
    $sslExpiry = if ($data.SslExpiry) { "'$($data.SslExpiry)'" } else { "NULL" }
    $bannerSql = if ($banner) { "'$banner'" } else { "NULL" }
    
    $sql = @"
UPDATE "Observations" 
SET "Banner" = $bannerSql,
    "ServiceName" = $serviceName,
    "HttpTitle" = $httpTitle,
    "HttpServer" = $httpServer,
    "HttpStatusCode" = $httpStatusCode,
    "SslSubject" = $sslSubject,
    "SslIssuer" = $sslIssuer,
    "SslExpiry" = $sslExpiry,
    "Tier" = 1
WHERE "TargetId" = '$($data.IP)' 
  AND "Port" = $($data.Port)
  AND "State" = 'open'
  AND "Tier" = 0;
"@
    
    docker exec nadosh-postgres psql -U nadosh -d nadosh -c $sql | Out-Null
    Write-Host "✓ Updated $($data.IP):$($data.Port)" -ForegroundColor Green
}

Write-Host "`n=== Enrichment Complete ===" -ForegroundColor Cyan
Write-Host "Run: docker exec nadosh-postgres psql -U nadosh -d nadosh -c ""SELECT \""TargetId\"", \""Port\"", \""ServiceName\"", \""Banner\"", \""HttpTitle\"" FROM \""Observations\"" WHERE \""Tier\"" = 1 ORDER BY \""TargetId\""::inet, \""Port\"";"" to verify"
