# Nadosh.Agents - AI-Powered Penetration Testing Orchestration

## Overview

**Nadosh.Agents** is an autonomous penetration testing orchestration engine that combines:
- **Kali Linux pentest tools** (nmap, nikto, sqlmap, nuclei, ffuf, etc.)
- **AI-driven decision making** (OpenAI GPT-4o via Semantic Kernel)
- **Phase-based assessment workflow** (Recon → Enumeration → Prioritization → Validation → Reporting)
- **Safety boundaries** with policy gates, scope validation, and approval requirements

## Architecture

### Core Components

1. **GetCapabilitiesToolsAgent**
   - Discovers available pentest tools in the Kali container
   - Returns structured tool manifests with capabilities, flags, and usage hints
   - Filters tools by assessment phase

2. **ExecuteCommandAgent**
   - Executes pentest commands with safety checks
   - Validates targets against authorized scope
   - Enforces timeouts and captures output
   - Sanitizes commands to prevent shell injection

3. **ParseAndPlanAgent**
   - AI-powered analysis of command outputs using GPT-4o
   - Extracts findings (vulnerabilities, misconfigurations, exposures)
   - Plans next assessment steps based on phase goals
   - Structured JSON responses for reliable automation

4. **PhaseOrchestrationEngine**
   - Coordinates multi-phase assessment workflow
   - Manages phase transitions and iteration limits
   - Integrates with Nadosh evidence collection
   - Updates AssessmentRun status in real-time

### Assessment Phases

| Phase | Goal | Example Tools | AI Objectives |
|-------|------|---------------|---------------|
| **Recon** | Discover hosts, ports, services | nmap, masscan | Identify attack surface, open ports, service versions |
| **Enumeration** | Service-specific discovery | nikto, ffuf, whatweb, gobuster | Find web tech stack, directories, API endpoints |
| **Prioritization** | CVE correlation, risk scoring | nuclei, testssl | Correlate findings with CVE database, assess severity |
| **Validation** | Safe vulnerability confirmation | sslscan, configuration audits | Validate high-severity findings (no exploits!) |
| **Reporting** | Evidence compilation | N/A | Synthesize findings, score risk, generate reports |

### Safety Boundaries

✅ **Allowed**:
- Network scanning (nmap, masscan)
- Service enumeration (nikto, whatweb)
- Directory brute forcing (ffuf, gobuster)
- CVE correlation (nuclei with safe templates)
- TLS/SSL auditing (sslscan, testssl.sh)
- Configuration validation

❌ **Forbidden**:
- Exploit payload execution
- Remote code execution
- Data exfiltration
- Credential brute forcing without explicit approval
- Destructive actions (DoS, data modification)

All commands are:
- Validated against authorized scope
- Sanitized to prevent shell injection
- Time-limited (default 5 minutes)
- Logged for audit trail

## Configuration

### Environment Variables

```bash
# Database
ConnectionStrings__DefaultConnection=Host=postgres;Database=nadosh;...

# Redis
ConnectionStrings__Redis=redis:6379

# OpenAI API
OPENAI_API_KEY=sk-...
OpenAI__Model=gpt-4o

# Agent Settings
AssessmentAgent__MaxIterationsPerPhase=10
AssessmentAgent__CommandTimeoutSeconds=300
AssessmentAgent__EnableScopeValidation=true
```

### Docker Compose

```yaml
agent-workers:
  build:
    context: .
    dockerfile: Dockerfile.agents
  environment:
    - OPENAI_API_KEY=${OPENAI_API_KEY}
    - OpenAI__Model=gpt-4o
  cap_add:
    - NET_RAW  # For nmap/masscan
    - NET_ADMIN
  volumes:
    - agent_evidence:/data/evidence
```

## Available Tools

### Network Scanning
- **nmap** - Network exploration and security auditing
- **masscan** - Ultra-fast TCP port scanner
- **netcat** - Network debugging and exploration

### Web Scanning
- **nikto** - Web server vulnerability scanner
- **ffuf** - Fast web fuzzer for content discovery
- **gobuster** - Directory/DNS/VHost bruteforcing
- **whatweb** - Web technology identification
- **wfuzz** - Web application fuzzer

### Vulnerability Scanning
- **nuclei** - Fast vulnerability scanner using templates
- **sslscan** - SSL/TLS cipher and protocol scanner
- **testssl.sh** - Testing TLS/SSL encryption

### Enumeration
- **enum4linux** - Windows/Samba enumeration
- **sqlmap** - SQL injection detection (use with caution)

### Utilities
- **jq** - JSON processor for parsing tool output
- **curl/wget** - HTTP clients
- **python3** with impacket, requests, beautifulsoup4

## Usage

### 1. Submit Assessment Run via API

```bash
curl -X POST http://localhost:5000/v1/AssessmentRuns \
  -H "X-API-Key: your-api-key" \
  -H "Content-Type: application/json" \
  -d '{
    "targetScope": "192.168.1.0/24",
    "environment": "lab",
    "requestedTools": ["asset.discovery.reconcile", "vulnerability.cve.correlate"],
    "scopeTags": ["authorized-scope", "lab-network"]
  }'
```

### 2. Agent Workflow

1. **Queue Consumer** - `AgentAssessmentWorker` dequeues assessment runs
2. **Tool Discovery** - Probes container for available Kali tools
3. **Phase Execution** - Iterates through Recon → Enum → Validation
4. **AI Planning** - GPT-4o analyzes outputs, extracts findings, plans next commands
5. **Command Execution** - Runs tools with scope validation and timeout
6. **Evidence Collection** - Compiles findings into structured evidence bundle
7. **Status Update** - Marks run as Completed/Failed with summary

### 3. Monitor Progress

```bash
# Get assessment run status
curl http://localhost:5000/v1/AssessmentRuns/{runId} \
  -H "X-API-Key: your-api-key"

# Get evidence bundle
curl http://localhost:5000/v1/AssessmentRuns/{runId}/evidence \
  -H "X-API-Key: your-api-key"

# Get agent context
curl http://localhost:5000/v1/AssessmentRuns/{runId}/agent-context \
  -H "X-API-Key: your-api-key"
```

## AI Prompting Strategy

### System Prompts by Phase

Each phase uses specialized system prompts:

- **Recon**: Focus on attack surface identification
- **Enumeration**: Service-specific vulnerability discovery
- **Validation**: Safe, non-destructive confirmation only
- **Reporting**: Risk scoring and remediation guidance

### Structured Responses

AI agent returns JSON:
```json
{
  "extractedFindings": [
    {
      "type": "http-server",
      "severity": "medium",
      "description": "Apache 2.4.41 with mod_status enabled",
      "target": "192.168.1.10:80",
      "metadata": { "version": "2.4.41", "modules": ["mod_status"] }
    }
  ],
  "continuePhase": true,
  "advancePhase": false,
  "nextCommands": [
    {
      "command": "nikto",
      "arguments": ["-h", "192.168.1.10", "-p", "80", "-Format", "json"],
      "targetScope": "192.168.1.0/24"
    }
  ],
  "reasoning": "Apache server detected with mod_status. Running nikto for vulnerability scan.",
  "newObjectives": ["Check mod_status disclosure", "Test for known Apache CVEs"]
}
```

## Development

### Build Docker Image

```bash
docker build -f Dockerfile.agents -t nadosh-agents:latest .
```

### Run Locally (requires Kali Linux or macOS/Linux with tools installed)

```bash
cd Nadosh.Agents
dotnet run
```

### Test Individual Agents

```csharp
// Discover tools
var capabilitiesAgent = new GetCapabilitiesToolsAgent(logger);
var tools = await capabilitiesAgent.DiscoverToolsAsync();

// Execute command
var executeAgent = new ExecuteCommandAgent(logger, policyService);
var result = await executeAgent.ExecuteAsync(new CommandExecutionRequest
{
    Command = "nmap",
    Arguments = new() { "-sV", "-p", "80,443", "192.168.1.10" },
    TargetScope = "192.168.1.0/24"
});

// Parse and plan
var planAgent = new ParseAndPlanAgent(logger, kernel);
var plan = await planAgent.ParseAndPlanAsync(context);
```

## Security Considerations

1. **Scope Validation**: All targets validated against authorized CIDR/scope
2. **Command Sanitization**: Shell metacharacters rejected
3. **Timeout Enforcement**: Commands killed after timeout
4. **Approval Gates**: Active validation requires approval reference
5. **Audit Logging**: All commands logged with timestamps and results
6. **Container Isolation**: Agents run in isolated Docker containers
7. **NET_RAW Capability**: Only granted for necessary network tools

## Roadmap

- [ ] Add custom tool definitions via configuration
- [ ] Support for additional LLM providers (Azure OpenAI, Anthropic Claude)
- [ ] Real-time streaming of command output
- [ ] Phase-specific risk scoring
- [ ] Integration with MITRE ATT&CK framework
- [ ] Custom nuclei template repository
- [ ] Scheduled/recurring assessments
- [ ] Multi-target parallel execution

## Contributing

See [CONTRIBUTING.md](../CONTRIBUTING.md) for development guidelines.

## License

MIT License - see [LICENSE](../LICENSE)
