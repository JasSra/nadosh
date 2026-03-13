<#
.SYNOPSIS
    Install Nadosh Edge Agent on Windows or Linux

.DESCRIPTION
    Downloads and installs the Nadosh edge agent, configures it to connect
    to the mothership control plane, and registers it as a service.

.PARAMETER MothershipUrl
    Base URL of the mothership API (e.g., https://nadosh.example.com)

.PARAMETER ApiKey
    API key for mothership authentication

.PARAMETER SiteId
    Site identifier for this edge location (e.g., "aws-us-east-1", "office-hq")

.PARAMETER AgentId
    Unique agent identifier (auto-generated if not provided)

.PARAMETER InstallPath
    Installation directory (default: C:\Program Files\Nadosh on Windows, /opt/nadosh on Linux)

.PARAMETER WorkerRoles
    Comma-separated worker roles to enable (default: "discovery,scanner,enrichment")

.EXAMPLE
    .\install-edge-agent.ps1 -MothershipUrl "https://nadosh.corp.com" -ApiKey "key123" -SiteId "aws-east"

.EXAMPLE
    pwsh install-edge-agent.ps1 -MothershipUrl "https://nadosh.corp.com" -ApiKey "key123" -SiteId "datacenter-1" -WorkerRoles "discovery,scanner"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$MothershipUrl,
    
    [Parameter(Mandatory=$true)]
    [string]$ApiKey,
    
    [Parameter(Mandatory=$true)]
    [string]$SiteId,
    
    [Parameter(Mandatory=$false)]
    [string]$AgentId = "",
    
    [Parameter(Mandatory=$false)]
    [string]$InstallPath = "",
    
    [Parameter(Mandatory=$false)]
    [string]$WorkerRoles = "discovery,scanner,enrichment"
)

$ErrorActionPreference = "Stop"

# Detect platform
$IsLinux = $PSVersionTable.PSVersion.Major -ge 6 -and $PSVersionTable.Platform -eq "Unix"
$IsWindows = !$IsLinux

Write-Host "=== Nadosh Edge Agent Installer ===" -ForegroundColor Cyan
Write-Host "Platform: $(if ($IsWindows) { 'Windows' } else { 'Linux' })"
Write-Host "Mothership: $MothershipUrl"
Write-Host "Site: $SiteId"

# Set default install path
if (-not $InstallPath) {
    $InstallPath = if ($IsWindows) { "C:\Program Files\Nadosh" } else { "/opt/nadosh" }
}

# Generate agent ID if not provided
if (-not $AgentId) {
    $AgentId = "$SiteId-$(New-Guid | Select-Object -ExpandProperty Guid | ForEach-Object { $_.ToString().Substring(0,8) })"
    Write-Host "Generated AgentId: $AgentId" -ForegroundColor Yellow
}

# Create install directory
Write-Host "`nCreating installation directory: $InstallPath"
if (!(Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
}

# Download latest worker binary
Write-Host "`nDownloading Nadosh.Workers binary..."
$runtime = if ($IsWindows) { "win-x64" } else { "linux-x64" }
$binaryName = if ($IsWindows) { "Nadosh.Workers.exe" } else { "Nadosh.Workers" }
$binaryPath = Join-Path $InstallPath $binaryName

# Note: In production, download from releases. For now, prompt to build locally
Write-Host "TODO: In production, download from GitHub releases" -ForegroundColor Yellow
Write-Host "For now, please build the worker locally:" -ForegroundColor Yellow
Write-Host "  dotnet publish Nadosh.Workers -c Release -r $runtime --self-contained -o `"$InstallPath`"" -ForegroundColor Cyan

# Create appsettings.json
Write-Host "`nCreating appsettings.json configuration..."
$appSettings = @{
    "Logging" = @{
        "LogLevel" = @{
            "Default" = "Information"
            "Microsoft" = "Warning"
        }
    }
    "ConnectionStrings" = @{
        "DefaultConnection" = "Host=localhost;Database=nadosh;Username=nadosh;Password=CHANGE_ME"
    }
    "EdgeControlPlane" = @{
        "Enabled" = $true
        "MothershipBaseUrl" = $MothershipUrl
        "ApiKey" = $ApiKey
        "SiteId" = $SiteId
        "AgentId" = $AgentId
        "HeartbeatIntervalSeconds" = 60
        "TaskPollIntervalSeconds" = 30
        "TaskLeaseDurationSeconds" = 300
        "AdvertisedCapabilities" = $WorkerRoles.Split(',') | ForEach-Object { $_.Trim() }
    }
    "WorkerRoles" = @{
        "EnabledRoles" = $WorkerRoles.Split(',') | ForEach-Object { $_.Trim() }
    }
} | ConvertTo-Json -Depth 10

$configPath = Join-Path $InstallPath "appsettings.json"
$appSettings | Out-File -FilePath $configPath -Encoding UTF8
Write-Host "Configuration saved to: $configPath" -ForegroundColor Green

# Install as service
Write-Host "`nInstalling as service..."

if ($IsWindows) {
    # Windows Service
    $serviceName = "NadoshEdgeAgent"
    $serviceDisplayName = "Nadosh Edge Agent ($AgentId)"
    
    # Check if service exists
    $existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($existingService) {
        Write-Host "Stopping existing service..."
        Stop-Service -Name $serviceName -Force
        Write-Host "Removing existing service..."
        sc.exe delete $serviceName
        Start-Sleep -Seconds 2
    }
    
    Write-Host "Creating Windows service..."
    New-Service -Name $serviceName `
        -DisplayName $serviceDisplayName `
        -BinaryPathName "`"$binaryPath`"" `
        -StartupType Automatic `
        -Description "Nadosh edge agent for site $SiteId"
    
    Write-Host "Starting service..."
    Start-Service -Name $serviceName
    
    Write-Host "`nService installed successfully!" -ForegroundColor Green
    Write-Host "Service Name: $serviceName"
    Write-Host "Status: $(Get-Service -Name $serviceName | Select-Object -ExpandProperty Status)"
    Write-Host "`nTo manage the service:"
    Write-Host "  Start:   Start-Service $serviceName"
    Write-Host "  Stop:    Stop-Service $serviceName"
    Write-Host "  Restart: Restart-Service $serviceName"
    Write-Host "  Logs:    Get-EventLog -LogName Application -Source $serviceName -Newest 50"
}
else {
    # Linux systemd
    $serviceName = "nadosh-edge-agent"
    $serviceFile = "/etc/systemd/system/$serviceName.service"
    
    Write-Host "Creating systemd service unit..."
    $serviceContent = @"
[Unit]
Description=Nadosh Edge Agent ($AgentId)
After=network.target

[Service]
Type=notify
WorkingDirectory=$InstallPath
ExecStart=$binaryPath
Restart=always
RestartSec=10
User=nadosh
Environment=DOTNET_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
"@
    
    # Create service file (requires sudo)
    Write-Host "Writing service file to $serviceFile (requires sudo)..."
    $serviceContent | sudo tee $serviceFile | Out-Null
    
    # Create nadosh user if doesn't exist
    Write-Host "Creating nadosh user..."
    sudo useradd -r -s /bin/false nadosh 2>$null
    
    # Set permissions
    Write-Host "Setting permissions..."
    sudo chown -R nadosh:nadosh $InstallPath
    sudo chmod +x $binaryPath
    
    # Enable and start service
    Write-Host "Enabling service..."
    sudo systemctl daemon-reload
    sudo systemctl enable $serviceName
    
    Write-Host "Starting service..."
    sudo systemctl start $serviceName
    
    Write-Host "`nService installed successfully!" -ForegroundColor Green
    Write-Host "Service Name: $serviceName"
    Write-Host "`nTo manage the service:"
    Write-Host "  Status:  sudo systemctl status $serviceName"
    Write-Host "  Start:   sudo systemctl start $serviceName"
    Write-Host "  Stop:    sudo systemctl stop $serviceName"
    Write-Host "  Restart: sudo systemctl restart $serviceName"
    Write-Host "  Logs:    sudo journalctl -u $serviceName -f"
}

Write-Host "`n=== Installation Complete ===" -ForegroundColor Cyan
Write-Host "`nAgent Configuration:"
Write-Host "  Site ID:      $SiteId"
Write-Host "  Agent ID:     $AgentId"
Write-Host "  Mothership:   $MothershipUrl"
Write-Host "  Install Path: $InstallPath"
Write-Host "  Config:       $configPath"
Write-Host "`nThe agent will automatically:"
Write-Host "  1. Enroll with the mothership"
Write-Host "  2. Send heartbeats every 60 seconds"
Write-Host "  3. Poll for authorized tasks every 30 seconds"
Write-Host "  4. Execute tasks matching its capabilities: $WorkerRoles"
Write-Host "`nMonitor enrollment status via mothership API:"
Write-Host "  GET $MothershipUrl/api/edge/agents" -ForegroundColor Cyan
