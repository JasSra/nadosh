# Edge Agent Installation - Admin Privilege Handling

## Problem Solved

When users tried to install the edge agent without administrator privileges, the script would fail with a permission error when trying to create directories in `C:\Program Files`.

## Solutions Implemented

### 1. Interactive Choice Menu

When the script detects it's not running as Administrator, it now presents three options:

```
WARNING: Not running as Administrator

Choose installation option:
  [1] Auto-elevate to Administrator (recommended)
  [2] Install to user directory (no admin required)
  [3] Exit and run manually as Administrator

Enter choice (1-3):
```

### 2. Option 1: Auto-Elevation

**What it does:**
- Automatically relaunches PowerShell with administrator privileges
- Preserves all environment variables (API key, Site ID, etc.)
- Prompts for UAC approval
- Installs as Windows Service
- Installs to `C:\Program Files\Nadosh`

**User experience:**
```powershell
iwr http://mothership/api/edge/install.ps1 | iex
# Select option [1]
# → UAC prompt appears
# → Installation proceeds with admin rights
```

**Technical implementation:**
```powershell
$command = "$envVars iwr $scriptUrl -UseBasicParsing | iex"
Start-Process powershell -Verb RunAs -ArgumentList "-NoExit","-Command",$command
```

### 3. Option 2: User Directory Installation

**What it does:**
- Installs to `%LOCALAPPDATA%\Nadosh` (no admin required)
- Creates a Scheduled Task instead of Windows Service
- Task runs at startup as current user
- Full functionality without admin privileges

**Benefits:**
- Works in restricted environments
- Perfect for testing/development
- No IT department approval needed
- Portable installation

**Technical implementation:**
```powershell
$InstallPath = "$env:LOCALAPPDATA\Nadosh"

# Creates scheduled task
$action = New-ScheduledTaskAction -Execute $binaryPath -WorkingDirectory $InstallPath
$trigger = New-ScheduledTaskTrigger -AtStartup
Register-ScheduledTask -TaskName "NadoshEdgeAgent" -Action $action -Trigger $trigger
Start-ScheduledTask -TaskName "NadoshEdgeAgent"
```

### 4. Option 3: Manual Instructions

Provides clear step-by-step instructions for manually running as Administrator.

## Installation Paths Comparison

| Method | Install Location | Runs As | Privileges | Startup |
|--------|-----------------|---------|-----------|---------|
| **Admin (Service)** | `C:\Program Files\Nadosh` | SYSTEM | High | Windows Service |
| **User (Task)** | `%LOCALAPPDATA%\Nadosh` | Current User | Standard | Scheduled Task |
| **Linux (systemd)** | `/opt/nadosh` | nadosh user | root required | systemd unit |

## Usage Examples

### Auto-Elevation with Environment Variables

```powershell
# Set configuration
$env:NADOSH_API_KEY = "your-api-key"
$env:NADOSH_SITE_ID = "office-seattle"

# Run installer (will prompt for choice)
iwr http://mothership/api/edge/install.ps1 | iex

# Select [1] for auto-elevation
# UAC prompt appears → approve → installation completes
```

### Silent User Directory Installation

```powershell
# For automated deployment without admin
echo "2" | iwr http://mothership/api/edge/install.ps1 | iex
```

### Enterprise Deployment (Pre-elevated)

```powershell
# Already running as Administrator
# Script detects admin rights and proceeds directly
iwr http://mothership/api/edge/install.ps1 | iex
# No menu shown, installs as service immediately
```

## Scheduled Task Management

When installed as a scheduled task (Option 2), use these commands:

```powershell
# Start the agent
Start-ScheduledTask -TaskName "NadoshEdgeAgent"

# Stop the agent
Stop-ScheduledTask -TaskName "NadoshEdgeAgent"

# Check status
Get-ScheduledTask -TaskName "NadoshEdgeAgent" | Select-Object State,LastRunTime,NextRunTime

# View task details
Get-ScheduledTaskInfo -TaskName "NadoshEdgeAgent"

# Remove the agent
Unregister-ScheduledTask -TaskName "NadoshEdgeAgent" -Confirm:$false
Remove-Item "$env:LOCALAPPDATA\Nadosh" -Recurse -Force
```

## Service Management (Admin Installation)

When installed as a Windows Service:

```powershell
# Start the service
Start-Service NadoshEdgeAgent

# Stop the service
Stop-Service NadoshEdgeAgent

# Restart the service
Restart-Service NadoshEdgeAgent

# Check status
Get-Service NadoshEdgeAgent

# View logs
Get-EventLog -LogName Application -Source NadoshEdgeAgent -Newest 50

# Remove the service
Stop-Service NadoshEdgeAgent
sc.exe delete NadoshEdgeAgent
Remove-Item "C:\Program Files\Nadosh" -Recurse -Force
```

## Comparison: Service vs Scheduled Task

### Windows Service (Admin Required)

**Pros:**
- Runs as SYSTEM account (high privileges)
- Standard enterprise deployment method
- Better integration with Windows logging
- Automatic dependency management
- Recovery options built-in

**Cons:**
- Requires administrator privileges
- Harder to debug (no console output)
- IT approval often needed

### Scheduled Task (No Admin)

**Pros:**
- No administrator privileges required
- Runs as current user (easier debugging)
- Console output visible
- Easy to modify/remove
- Perfect for development

**Cons:**
- Lower privileges (may limit capabilities)
- Less robust than service
- Not standard for production deployments

## Cross-Platform Behavior

### Windows
- Offers admin/user choice
- Service or Scheduled Task
- UAC integration

### Linux
- Always uses systemd
- Requires sudo for system-wide install
- Creates dedicated `nadosh` user
- Standard Unix service pattern

## Best Practices

### Development/Testing
→ Use **Option 2** (User Directory)
- Fast iteration
- Easy to remove/reinstall
- No admin friction

### Production Deployment
→ Use **Option 1** (Auto-Elevation)
- Proper service installation
- Runs as SYSTEM
- Enterprise-ready

### Restricted Environments
→ Use **Option 2** (User Directory)
- Works without admin rights
- Can be deployed by users
- Still fully functional

## Security Considerations

### Admin Installation
- Service runs as SYSTEM
- Can perform network scans requiring raw sockets
- Can access all system resources
- Standard audit trail via Event Log

### User Installation
- Task runs as current user
- Limited to user's privileges
- Network scans may have limitations
- Audit trail via Task Scheduler history

Both methods:
- ✅ Require API key for mothership authentication
- ✅ Support site-based authorization
- ✅ Use encrypted HTTPS communication (in production)
- ✅ Follow principle of least privilege for their context

## Troubleshooting

### UAC Prompt Doesn't Appear (Option 1)
- Check UAC is enabled: `Get-ItemProperty HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System -Name EnableLUA`
- Try running from regular PowerShell (not VS Code terminal)
- Use Option 2 instead

### Scheduled Task Not Starting
```powershell
# Check task status
Get-ScheduledTask -TaskName "NadoshEdgeAgent" | fl *

# Check last run result
Get-ScheduledTaskInfo -TaskName "NadoshEdgeAgent" | fl LastRunTime,LastTaskResult

# Common results:
# 0x0 = Success
# 0x1 = Task not ready
# 0x41301 = Task not running
```

### Binary Not Found
The script currently prompts to build locally. In production:
```powershell
# Build the worker
dotnet publish Nadosh.Workers -c Release -r win-x64 --self-contained -o "$InstallPath"
```

## Future Enhancements

- [ ] Download pre-built binaries from GitHub releases
- [ ] Support for Windows Service wrapper (NSSM)
- [ ] Container deployment option
- [ ] Automatic updates
- [ ] Health check integration
- [ ] Multi-site agent support

## Conclusion

The installation script now provides a flexible, user-friendly deployment experience that:
- ✅ Works with or without admin privileges
- ✅ Offers automatic elevation when possible
- ✅ Provides fallback options for restricted environments
- ✅ Maintains full functionality in both modes
- ✅ Follows Windows best practices
