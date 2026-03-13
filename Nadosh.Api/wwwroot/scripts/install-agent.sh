#!/bin/bash
# Nadosh Edge Agent Installer
# Usage: curl -sSL https://your-mothership/edge/install.sh | bash
# Or with env vars: NADOSH_API_KEY=key NADOSH_SITE_ID=site curl -sSL https://your-mothership/edge/install.sh | bash

set -e

MOTHERSHIP_URL="{{MOTHERSHIP_URL}}"
INSTALL_PATH="/opt/nadosh"
SERVICE_NAME="nadosh-edge-agent"

echo "=== Nadosh Edge Agent Installer ==="
echo "Mothership: $MOTHERSHIP_URL"

# Check prerequisites
if ! command -v dotnet &> /dev/null; then
    echo "ERROR: .NET runtime not found. Install .NET 8+ first:"
    echo "  https://dot.net/download"
    exit 1
fi

# Get parameters from env vars or prompt
API_KEY="${NADOSH_API_KEY:-}"
SITE_ID="${NADOSH_SITE_ID:-}"
WORKER_ROLES="${NADOSH_WORKER_ROLES:-discovery,scanner,enrichment}"

if [ -z "$API_KEY" ]; then
    read -p "Enter API Key: " API_KEY
fi

if [ -z "$SITE_ID" ]; then
    read -p "Enter Site ID (e.g., aws-east, office-hq): " SITE_ID
fi

# Generate agent ID
AGENT_ID="$SITE_ID-$(cat /dev/urandom | tr -dc 'a-z0-9' | fold -w 8 | head -n 1)"

echo ""
echo "Configuration:"
echo "  Site ID:    $SITE_ID"
echo "  Agent ID:   $AGENT_ID"
echo "  Install To: $INSTALL_PATH"
echo "  Roles:      $WORKER_ROLES"

# Create install directory
echo ""
echo "Creating directory: $INSTALL_PATH"
sudo mkdir -p $INSTALL_PATH

# Download worker binary
echo "Downloading worker binary..."
BINARY_PATH="$INSTALL_PATH/Nadosh.Workers"
DOWNLOAD_URL="$MOTHERSHIP_URL/downloads/nadosh-workers-linux-x64.tar.gz"

# Try to download from mothership
if curl -f -sSL "$DOWNLOAD_URL" -o /tmp/nadosh-workers.tar.gz 2>/dev/null; then
    sudo tar -xzf /tmp/nadosh-workers.tar.gz -C $INSTALL_PATH
    rm /tmp/nadosh-workers.tar.gz
    echo "✓ Downloaded from mothership"
else
    echo "WARNING: Could not download from $DOWNLOAD_URL"
    echo "Please build locally:"
    echo "  dotnet publish Nadosh.Workers -c Release -r linux-x64 --self-contained -o $INSTALL_PATH"
    echo ""
    read -p "Press Enter after building, or Ctrl+C to cancel..."
    
    if [ ! -f "$BINARY_PATH" ]; then
        echo "ERROR: Worker binary not found at $BINARY_PATH"
        exit 1
    fi
fi

# Create appsettings.json
echo "Creating configuration..."
sudo tee $INSTALL_PATH/appsettings.json > /dev/null <<EOF
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=nadosh_edge;Username=nadosh;Password=CHANGE_ME"
  },
  "EdgeControlPlane": {
    "Enabled": true,
    "MothershipBaseUrl": "$MOTHERSHIP_URL",
    "ApiKey": "$API_KEY",
    "SiteId": "$SITE_ID",
    "AgentId": "$AGENT_ID",
    "HeartbeatIntervalSeconds": 60,
    "TaskPollIntervalSeconds": 30,
    "TaskLeaseDurationSeconds": 300,
    "AdvertisedCapabilities": ["$(echo $WORKER_ROLES | sed 's/,/","/g')"]
  },
  "WorkerRoles": {
    "EnabledRoles": ["$(echo $WORKER_ROLES | sed 's/,/","/g')"]
  }
}
EOF

# Create systemd service
echo "Installing systemd service..."
sudo tee /etc/systemd/system/$SERVICE_NAME.service > /dev/null <<EOF
[Unit]
Description=Nadosh Edge Agent ($AGENT_ID)
After=network.target

[Service]
Type=notify
WorkingDirectory=$INSTALL_PATH
ExecStart=$BINARY_PATH
Restart=always
RestartSec=10
User=nadosh
Environment=DOTNET_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
EOF

# Create user
echo "Creating service user..."
sudo useradd -r -s /bin/false nadosh 2>/dev/null || true
sudo chown -R nadosh:nadosh $INSTALL_PATH
sudo chmod +x $BINARY_PATH

# Enable and start service
echo "Enabling and starting service..."
sudo systemctl daemon-reload
sudo systemctl enable $SERVICE_NAME
sudo systemctl start $SERVICE_NAME

echo ""
echo "✓ Service installed successfully!"
echo "  Name: $SERVICE_NAME"
echo "  Status: $(sudo systemctl is-active $SERVICE_NAME)"

echo ""
echo "=== Installation Complete ==="
echo ""
echo "Agent will automatically:"
echo "  1. Enroll with mothership"
echo "  2. Send heartbeats every 60s"
echo "  3. Poll for tasks every 30s"
echo "  4. Execute authorized tasks"
echo ""
echo "Verify enrollment:"
echo "  $MOTHERSHIP_URL/edge.html"
echo ""
echo "Manage service:"
echo "  sudo systemctl status $SERVICE_NAME"
echo "  sudo systemctl stop $SERVICE_NAME"
echo "  sudo systemctl restart $SERVICE_NAME"
echo "  sudo journalctl -u $SERVICE_NAME -f"
