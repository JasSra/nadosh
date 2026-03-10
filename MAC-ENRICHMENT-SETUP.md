# MAC Address Enrichment Setup Guide

This feature enriches your network scans with device vendor identification using MAC addresses.

## Overview

The MAC enrichment system:
1. **Collects** MAC addresses via ARP scanning during port discovery
2. **Looks up** vendor information using IEEE OUI database (Wireshark format)
3. **Enriches** targets with vendor names and device type hints
4. **Displays** vendor info in API responses

## Setup Instructions

### 1. Download the MAC Vendor Database

Run the download script to fetch Wireshark's MAC vendor database:

```powershell
.\scripts\download-mac-database.ps1
```

This downloads the `manuf` file from: https://gitlab.com/wireshark/wireshark/-/raw/master/manuf

### 2. Place the Database File

Copy the `manuf` file to your application directories:

```powershell
# For Workers
Copy-Item .\scripts\manuf .\Nadosh.Workers\bin\Debug\net10.0\

# For API
Copy-Item .\scripts\manuf .\Nadosh.Api\bin\Debug\net10.0\

# For Docker deployments, add to Dockerfiles
```

**For Docker**: Add this line to both Dockerfiles:
```dockerfile
COPY scripts/manuf /app/manuf
```

### 3. Apply Database Migration

```powershell
cd Nadosh.Infrastructure
dotnet ef database update --startup-project ../Nadosh.Api
```

### 4. Enable MAC Enrichment Worker

Update your docker-compose.yml or add environment variable:

```yaml
workers:
  environment:
    - WORKER_ROLE=all,mac-enrichment
```

Or run locally:
```powershell
$env:WORKER_ROLE="all,mac-enrichment"
dotnet run --project Nadosh.Workers
```

## How It Works

### ARP Scanning (Local Networks Only)

When DiscoveryWorker scans an IP on a local network (10.x.x.x, 192.168.x.x, 172.16-31.x.x):
1. Sends a ping to populate ARP cache
2. Queries system ARP table (`arp -a` on Windows, `ip neigh` on Linux)
3. Extracts MAC address
4. Queues MacEnrichmentJob

### Vendor Lookup

MacEnrichmentWorker processes queued jobs:
1. Looks up first 3 bytes (OUI) in manuf database
2. Matches vendor name (e.g., "Apple, Inc.", "Tesla, Inc.")
3. Infers device type from vendor patterns (smartphone, iot, networking, etc.)
4. Updates Target and recent Observations

### Device Type Inference

Based on vendor name patterns:
- **Apple**, Samsung, Google → `smartphone/tablet/laptop`
- **Tesla** → `vehicle/iot`
- **Cisco**, Ubiquiti, Netgear → `networking`
- **Nest**, Ring, Philips → `iot/home-automation`
- **Synology**, Western Digital → `nas`
- And many more...

## API Usage

### Get Enriched Exposure Data

```bash
curl -H "X-API-Key: dev-api-key-123" \
  http://localhost:5000/v1/exposures/192.168.4.116
```

Response includes MAC vendor info:
```json
{
  "targetIp": "192.168.4.116",
  "macAddress": "4C:FC:AA:01:02:03",
  "macVendor": "Tesla, Inc.",
  "deviceType": "vehicle/iot",
  "openPorts": [
    {
      "port": 80,
      "service": "http",
      "httpTitle": "Tesla Wall Connector"
    }
  ]
}
```

## Database Fields

### Targets Table
- `MacAddress`: Normalized MAC (XX:XX:XX:XX:XX:XX)
- `MacVendor`: Manufacturer name
- `DeviceType`: Inferred device category
- `MacEnrichmentCompletedAt`: Enrichment timestamp

### Observations Table
- Same MAC fields for historical tracking

### CurrentExposures Table
- Cached MAC vendor data for fast API responses

## Updating the Vendor Database

The IEEE OUI database is updated regularly. Re-run the download script periodically:

```powershell
.\scripts\download-mac-database.ps1
```

Then restart your workers to reload the database.

## Limitations

1. **Local Networks Only**: ARP only works on the same subnet
2. **Requires Host Access**: Needs to run `arp` or `ip neigh` commands
3. **Cache Dependencies**: Some devices may not respond to ping
4. **Virtual MACs**: VMs and containers may have randomized MACs
5. **Privacy**: Modern devices may use MAC randomization

## Troubleshooting

### No MAC addresses detected

```powershell
# Check if ARP is working
arp -a

# Verify worker role
docker-compose logs workers | Select-String "MacEnrichment"

# Check database
docker exec nadosh-postgres psql -U nadosh -d nadosh -c \
  'SELECT "Ip", "MacAddress", "MacVendor" FROM "Targets" WHERE "MacAddress" IS NOT NULL;'
```

### Vendor not found

- Database may be missing (check `manuf` file exists)
- MAC might be from unregistered range
- MAC could be randomized/virtual

### Performance Impact

ARP scanning adds minimal overhead:
- Non-blocking (fire-and-forget after port scan)
- Only runs on local network IPs
- Cached in database after first resolution

## Example Devices Detected

| Vendor | Device Type | Common Devices |
|--------|-------------|----------------|
| Apple, Inc. | smartphone/tablet/laptop | iPhone, iPad, MacBook |
| Tesla, Inc. | vehicle/iot | Wall Connector, Vehicles |
| Ubiquiti Inc | networking | UniFi AP, EdgeRouter |
| Google, Inc. | smartphone/iot | Pixel, Nest Hub |
| Raspberry Pi | embedded/iot | Pi boards |
| Amazon | iot/smart-speaker | Echo, Ring |
| Sonos | iot/audio | Speakers |

