# Edge Agent Installation & C&C Guide

**Status**: ✅ Complete  
**Date**: March 13, 2026

## Overview

This guide covers the complete edge agent deployment workflow: installation script, registration endpoints, and the Command & Control dashboard.

---

## 1. Installation Script

**Location**: [scripts/install-edge-agent.ps1](scripts/install-edge-agent.ps1)

Cross-platform PowerShell script that:
- ✅ Works on Windows (PowerShell 5.1+) and Linux (PowerShell Core 7+)
- ✅ Installs agent as Windows Service or Linux systemd unit
- ✅ Auto-generates agent ID if not provided
- ✅ Creates appsettings.json with mothership configuration
- ✅ Sets up proper permissions and service auto-start

### Usage

```powershell
# Download script
curl -o install-edge-agent.ps1 https://nadosh.example.com/scripts/install-edge-agent.ps1

# Windows Installation
pwsh install-edge-agent.ps1 `
  -MothershipUrl "https://nadosh.example.com" `
  -ApiKey "your-api-key" `
  -SiteId "aws-us-east-1" `
  -WorkerRoles "discovery,scanner,enrichment"

# Linux Installation
pwsh install-edge-agent.ps1 \
  -MothershipUrl "https://nadosh.example.com" \
  -ApiKey "your-api-key" \
  -SiteId "datacenter-1" \
  -WorkerRoles "discovery,scanner"
```

### Parameters

| Parameter | Required | Description | Default |
|-----------|----------|-------------|---------|
| `MothershipUrl` | ✅ | Base URL of control plane API | - |
| `ApiKey` | ✅ | API key for authentication | - |
| `SiteId` | ✅ | Site identifier (e.g., "aws-east") | - |
| `AgentId` | ❌ | Unique agent ID | Auto-generated |
| `InstallPath` | ❌ | Installation directory | `C:\Program Files\Nadosh` (Win)<br>`/opt/nadosh` (Linux) |
| `WorkerRoles` | ❌ | Comma-separated capabilities | `discovery,scanner,enrichment` |

### What It Does

1. **Detects platform** (Windows/Linux)
2. **Creates install directory** with proper permissions
3. **Generates configuration** file with mothership settings
4. **Installs service**:
   - Windows: Creates service via `New-Service`, sets to automatic startup
   - Linux: Creates systemd unit file, enables auto-start
5. **Starts service** immediately
6. **Provides management commands** for service control

### Service Management

**Windows:**
```powershell
# View status
Get-Service NadoshEdgeAgent

# Control service
Start-Service NadoshEdgeAgent
Stop-Service NadoshEdgeAgent
Restart-Service NadoshEdgeAgent

# View logs
Get-EventLog -LogName Application -Source NadoshEdgeAgent -Newest 50
```

**Linux:**
```bash
# View status
sudo systemctl status nadosh-edge-agent

# Control service
sudo systemctl start nadosh-edge-agent
sudo systemctl stop nadosh-edge-agent
sudo systemctl restart nadosh-edge-agent

# View logs
sudo journalctl -u nadosh-edge-agent -f
```

---

## 2. Registration & Control-Plane Endpoints

### Agent-Facing Endpoints (Already Implemented)

**File**: [Nadosh.Api/Controllers/EdgeAgentsController.cs](Nadosh.Api/Controllers/EdgeAgentsController.cs)

These endpoints are called by edge agents automatically:

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/v1/edge-agents/enroll` | POST | Register new agent with mothership |
| `/v1/edge-agents/heartbeat` | POST | Send heartbeat, renew task leases |
| `/v1/edge-agents/{agentId}/tasks` | GET | Poll for pending authorized tasks |
| `/v1/edge-agents/{agentId}/tasks/{taskId}/claim` | POST | Claim task before execution |
| `/v1/edge-agents/{agentId}/tasks/{taskId}/complete` | POST | Report successful task completion |
| `/v1/edge-agents/{agentId}/tasks/{taskId}/fail` | POST | Report task failure |

### C&C Management Endpoints (NEW)

**File**: [Nadosh.Api/Controllers/EdgeCommandController.cs](Nadosh.Api/Controllers/EdgeCommandController.cs)

Human/operator-facing REST API for edge fleet management:

#### Sites Management

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `GET /api/edge/sites` | GET | List all edge sites |
| `GET /api/edge/sites/{siteId}` | GET | Get site details |
| `PUT /api/edge/sites/{siteId}` | PUT | Create or update site |
| `DELETE /api/edge/sites/{siteId}` | DELETE | Delete site (fails if agents enrolled) |

#### Agents Management

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `GET /api/edge/agents` | GET | List agents (filter by site/status) |
| `GET /api/edge/agents/{agentId}` | GET | Get agent details + task stats |
| `PATCH /api/edge/agents/{agentId}/status` | PATCH | Set agent status (maintenance/offline) |
| `DELETE /api/edge/agents/{agentId}` | DELETE | Delete agent, release tasks |
| `GET /api/edge/agents/{agentId}/activity` | GET | Get heartbeat activity timeline |

#### Task Management

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `GET /api/edge/tasks` | GET | List tasks (filter by site/agent/status) |
| `GET /api/edge/tasks/{taskId}` | GET | Get task details |
| `POST /api/edge/tasks` | POST | Create new authorized task |
| `POST /api/edge/tasks/{taskId}/cancel` | POST | Cancel pending/claimed task |
| `GET /api/edge/execution-records` | GET | Get agent-side execution buffer state |

#### Fleet Statistics

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `GET /api/edge/stats` | GET | Fleet overview: site/agent/task counts |

---

## 3. Command & Control Dashboard

**URL**: `http://localhost:5000/edge.html`  
**File**: [Nadosh.Api/wwwroot/edge.html](Nadosh.Api/wwwroot/edge.html)

Interactive web dashboard for edge fleet management with 4 tabs:

### 🖥️ Agents Tab

Real-time agent status table:
- Agent ID, Site, Status (Online/Offline/Maintenance)
- Platform (OS/Architecture)
- Advertised capabilities
- Last seen timestamp
- Actions: View details, Delete agent

**Features:**
- Color-coded status indicators (green=online, red=offline, yellow=maintenance)
- Auto-refresh every 30 seconds
- Minutes-since-last-seen calculation

### 📍 Sites Tab

Grid view of edge sites:
- Site name and ID
- Allowed CIDR ranges
- Allowed capabilities
- Create new site button

### 📋 Tasks Tab

Task queue management:
- Task ID, Site, Kind, Status
- Claimed agent assignment
- Creation timestamp
- Cancel button for pending/claimed tasks
- Create task button

### 📦 Install Tab

Complete installation guide:
- Download script command
- Example installation commands (Windows/Linux)
- Configuration parameters
- Verification steps
- API endpoint reference

**Dashboard Features:**
- Live stats in header (total agents, online count, pending tasks)
- Manual refresh button
- Auto-refresh every 30 seconds
- Responsive design with Tailwind CSS
- Alpine.js for reactivity

---

## Complete Workflow Example

### Step 1: Create Site (Optional)

Sites are auto-created on first agent enrollment, but you can pre-configure:

```bash
curl -X PUT https://nadosh.example.com/api/edge/sites/aws-east \
  -H "X-API-Key: your-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "AWS US-East-1",
    "allowedCidrs": ["10.0.0.0/8", "172.16.0.0/12"],
    "allowedCapabilities": ["discovery", "scanner", "enrichment"]
  }'
```

### Step 2: Install Edge Agent

Run installation script on target host:

```powershell
pwsh install-edge-agent.ps1 \
  -MothershipUrl "https://nadosh.example.com" \
  -ApiKey "your-key" \
  -SiteId "aws-east" \
  -AgentId "aws-east-scanner-01" \
  -WorkerRoles "discovery,scanner"
```

**Agent automatically:**
1. Enrolls with mothership (`POST /v1/edge-agents/enroll`)
2. Sends heartbeat every 60s (`POST /v1/edge-agents/heartbeat`)
3. Polls for tasks every 30s (`GET /v1/edge-agents/{agentId}/tasks`)

### Step 3: Verify Enrollment

**Via Dashboard:**
- Open `https://nadosh.example.com/edge.html`
- Click "Agents" tab
- Verify agent shows as "Online"

**Via API:**
```bash
curl https://nadosh.example.com/api/edge/agents \
  -H "X-API-Key: your-key"
```

### Step 4: Create Authorized Task

```bash
curl -X POST https://nadosh.example.com/api/edge/tasks \
  -H "X-API-Key: your-key" \
  -H "Content-Type: application/json" \
  -d '{
    "siteId": "aws-east",
    "taskKind": "DiscoveryScan",
    "scope": {
      "targets": ["10.0.1.0/24"],
      "ports": [22, 80, 443, 3389]
    },
    "requiredCapabilities": ["discovery"],
    "expiresInMinutes": 60
  }'
```

### Step 5: Monitor Execution

**Dashboard:**
- Tasks tab shows task status progression: Pending → Claimed → Completed
- Agents tab shows which agent claimed the task

**API:**
```bash
# Get task status
curl https://nadosh.example.com/api/edge/tasks/{taskId} \
  -H "X-API-Key: your-key"

# Get execution records (agent-side buffer)
curl https://nadosh.example.com/api/edge/execution-records?agentId=aws-east-scanner-01 \
  -H "X-API-Key: your-key"
```

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Mothership (Nadosh.Api)                  │
│                                                             │
│  ┌──────────────────┐      ┌────────────────────────────┐  │
│  │ EdgeAgentsCtrl   │      │ EdgeCommandCtrl (C&C)      │  │
│  │                  │      │                            │  │
│  │ POST /enroll     │      │ GET/PUT /sites             │  │
│  │ POST /heartbeat  │      │ GET/DELETE /agents         │  │
│  │ GET /tasks       │      │ GET/POST /tasks            │  │
│  │ POST /claim      │      │ GET /stats                 │  │
│  │ POST /complete   │      │                            │  │
│  └──────────────────┘      └────────────────────────────┘  │
│           ▲                          ▲                      │
│           │                          │                      │
│           │ Outbound                 │ HTTPS (Operators)    │
└───────────┼──────────────────────────┼──────────────────────┘
            │                          │
            │                    ┌─────▼──────┐
            │                    │ Dashboard  │
            │                    │ edge.html  │
            │                    └────────────┘
            │
   ┌────────▼─────────┐
   │ Edge Agent       │
   │ (Nadosh.Workers) │
   │                  │
   │ - Enroll         │
   │ - Heartbeat      │
   │ - Poll tasks     │
   │ - Execute        │
   │ - Upload results │
   └──────────────────┘
   Runs as service inside
   customer network
```

---

## Security Considerations

### Current Implementation
- ✅ API key authentication via `X-API-Key` header
- ✅ Agent enrollment with site/CIDR validation
- ✅ Task scope authorization (allowed targets/ports)
- ✅ Lease-based task claiming (prevents duplicate execution)
- ✅ Audit trail for all control-plane events

### Future Enhancements
- [ ] mTLS for agent authentication
- [ ] Per-agent API keys instead of shared site key
- [ ] Signed task payloads
- [ ] Agent capability attestation
- [ ] Encrypted task results upload

---

## Monitoring & Operations

### Health Checks

Agents automatically marked offline if:
- No heartbeat received for 5+ minutes
- Lease expiry recovery detects abandoned claims

### Task Lease Lifecycle

1. **Pending**: Task created, waiting for agent
2. **Claimed**: Agent claimed with lease token, `LeaseExpiresAt` set
3. **Lease Renewal**: Heartbeat extends `LeaseExpiresAt` by 5 minutes
4. **Execution**: Agent executes locally, buffers results
5. **Completed**: Upload succeeds, task finalized
6. **Expired/Failed**: Lease expires → recovery requeues or fails task

### Dashboard Metrics

Real-time stats in header:
- Total agents enrolled
- Online agent count
- Pending tasks count

Fleet overview (`GET /api/edge/stats`):
- Site count
- Agent status breakdown (online/offline/maintenance)
- Task status breakdown (pending/claimed/completed/failed)

---

## Files Created

| File | Purpose |
|------|---------|
| [scripts/install-edge-agent.ps1](scripts/install-edge-agent.ps1) | Cross-platform agent installer |
| [Nadosh.Api/Controllers/EdgeCommandController.cs](Nadosh.Api/Controllers/EdgeCommandController.cs) | C&C REST API (15 endpoints) |
| [Nadosh.Api/wwwroot/edge.html](Nadosh.Api/wwwroot/edge.html) | Interactive C&C dashboard |

## Quick Reference

**Install Agent:**
```powershell
pwsh install-edge-agent.ps1 -MothershipUrl "https://..." -ApiKey "..." -SiteId "..."
```

**Access Dashboard:**
```
https://your-mothership.com/edge.html
```

**Create Task:**
```bash
POST /api/edge/tasks
{
  "siteId": "aws-east",
  "taskKind": "DiscoveryScan",
  "scope": { "targets": ["10.0.0.0/24"] }
}
```

**Monitor Fleet:**
```bash
GET /api/edge/stats
GET /api/edge/agents
GET /api/edge/tasks
```
