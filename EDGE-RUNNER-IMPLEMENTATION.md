# Edge Runner Implementation Progress

Last updated: 2026-03-12

## Goal

Add a cross-platform edge-runner foundation so Nadosh can coordinate approved internal assessment work from a central mothership while executing inside networks that only allow outbound connectivity.

## What landed in this slice

### Core contracts and models

- `Nadosh.Core/Configuration/EdgeControlPlaneOptions.cs`
  - Worker/API configuration for outbound mothership sync.
- `Nadosh.Core/Interfaces/IAuditService.cs`
  - Shared audit writer abstraction for control-plane events.
- `Nadosh.Core/Interfaces/IEdgeControlPlaneService.cs`
  - Enrollment, heartbeat, and pending-task lookup contract.
- `Nadosh.Core/Models/EdgeSite.cs`
  - Site registration and allowed scope/capability metadata.
- `Nadosh.Core/Models/EdgeAgent.cs`
  - Durable agent identity, platform metadata, status, and advertised capabilities.
- `Nadosh.Core/Models/AuthorizedTask.cs`
  - Site/agent-scoped authorized task envelope with lease/expiry fields.
- `Nadosh.Core/Models/AuthorizedTaskKinds.cs`
  - Canonical task kind identifiers for the edge queue bridge.
- `Nadosh.Core/Models/AuthorizedTaskScope.cs`
  - Minimal scope contract for allowed targets, CIDRs, and ports.
- `Nadosh.Core/Models/EdgeTaskExecutionRecord.cs`
  - Durable local record of claimed authorized tasks, execution state, and upload retry metadata.
- `Nadosh.Core/Models/EdgeControlPlaneContracts.cs`
  - API/worker request-response payloads for enroll, heartbeat, task polling, task claim, completion, and failure.
- `Nadosh.Core/Services/AuthorizedTaskScopeEvaluator.cs`
  - Shared preflight scope validation for authorized task targets and ports.
- `Nadosh.Core/Interfaces/IEdgeTaskExecutionTracker.cs`
  - Abstraction for local execution buffering and upload retry tracking.

### Infrastructure and persistence

- `Nadosh.Infrastructure/Data/AuditService.cs`
  - Writes audit entries to `AuditEvents`.
- `Nadosh.Infrastructure/Data/EdgeControlPlaneService.cs`
  - Upserts sites/agents, records heartbeat state, and returns pending authorized tasks.
- `Nadosh.Infrastructure/Data/EdgeTaskExecutionTracker.cs`
  - Persists local task execution state and upload retry metadata.
- `Nadosh.Infrastructure/Data/NadoshDbContext.cs`
  - Added `EdgeSites`, `EdgeAgents`, `AuthorizedTasks`, and `EdgeTaskExecutionRecords` with EF configuration.
- `Nadosh.Infrastructure/InfrastructureServiceCollectionExtensions.cs`
  - Registered edge control-plane services, execution tracker, shared catalog, and `EdgeControlPlane` options.
- `Nadosh.Infrastructure/Migrations/*AddEdgeControlPlaneScaffolding*`
  - Schema migration for new edge control-plane tables.
- `Nadosh.Infrastructure/Migrations/*AddEdgeTaskExecutionBuffering*`
  - Schema migration for the durable local execution buffer.

### API surface

- `Nadosh.Api/Controllers/EdgeAgentsController.cs`
  - `POST /v1/edge-agents/enroll`
  - `POST /v1/edge-agents/heartbeat`
  - `GET /v1/edge-agents/{agentId}/tasks`
  - `POST /v1/edge-agents/{agentId}/tasks/{taskId}/claim`
  - `POST /v1/edge-agents/{agentId}/tasks/{taskId}/complete`
  - `POST /v1/edge-agents/{agentId}/tasks/{taskId}/fail`

### Worker host / edge sync

- `Nadosh.Workers/Program.cs`
  - Starts the outbound sync hosted service when `EdgeControlPlane:Enabled=true`.
- `Nadosh.Workers/Edge/EdgeControlPlaneSyncService.cs`
  - Performs outbound enrollment and heartbeat.
  - Polls pending authorized tasks.
  - Claims tasks with lease tokens before local dispatch.
  - Validates authorized target scope before queue injection.
  - Bridges supported task kinds into existing local worker queues.
  - Persists claimed tasks into the local execution buffer instead of immediately finalizing them centrally.
  - Advertises both worker-role capabilities and approved assessment tool ids.
  - Emits platform metadata for `win-x64` and `linux-x64` shipping targets.
- `Nadosh.Workers/Edge/EdgeTaskUploadService.cs`
  - Retries final completion/failure uploads from the local buffer back to the mothership.
- `Nadosh.Workers/DiscoveryWorker.cs`
- `Nadosh.Workers/Stage2Worker.cs`
- `Nadosh.Workers/MacEnrichmentWorker.cs`
  - Revalidate authorized scope during execution and mark durable completion/failure results locally.
- `Nadosh.Workers/appsettings.json`
- `Nadosh.Workers/appsettings.Development.json`
- `Nadosh.Api/appsettings.Development.json`
  - Added initial config scaffolding for local development.

## Cross-platform note

This slice is designed for .NET 10 worker binaries on both:

- `win-x64`
- `linux-x64`

Platform metadata is detected at runtime via `RuntimeInformation` and sent during enrollment/heartbeat.

## What is not implemented yet

- Strong agent trust (mTLS, signed enrollment, per-agent key rotation)
- Final result upload, resumable sync, or local evidence buffering
- Full evidence payload synchronization beyond completion/failure summaries
- Remote cancellation / kill-switch endpoints
- Operator approval workflow and issuance UX
- Lease renewal / recovery for tasks that remain claimed if an edge node dies mid-execution

## Supported bridge task kinds in this slice

- `nadosh.stage1.scan` -> local `Stage1ScanJob` -> final completion/failure upload after discovery finishes
- `nadosh.stage2.enrichment` -> local `Stage2EnrichmentJob` -> final completion/failure upload after enrichment finishes
- `nadosh.mac-enrichment` -> local `MacEnrichmentJob` -> final completion/failure upload after enrichment finishes

Task completion at the mothership now means **the local worker finished and the edge uploader successfully sent the final completion/failure state**. The current upload payload is still summary/metadata oriented rather than a full evidence bundle.

## Next recommended slices

1. Extend task coverage from queue-entry task kinds to higher-level approved tool workflows.
2. Add richer result/evidence upload contracts so central workflows receive normalized observations and evidence references.
3. Add lease renewal / abandoned-claim recovery for long-running or interrupted tasks.
4. Replace shared API-key trust with agent-specific credentials and revocation.
5. Add API/infrastructure tests around claim, lease validation, requeue, and task visibility.

## Validation completed

- `dotnet build Nadosh.Api/Nadosh.Api.csproj -v minimal`
- `dotnet build Nadosh.Workers/Nadosh.Workers.csproj -v minimal`
- `dotnet ef migrations add AddEdgeControlPlaneScaffolding --project Nadosh.Infrastructure/Nadosh.Infrastructure.csproj --startup-project Nadosh.Api/Nadosh.Api.csproj --context NadoshDbContext`
- `dotnet test Nadosh.Core.Tests/Nadosh.Core.Tests.csproj -v minimal`
- `dotnet ef migrations add AddEdgeTaskExecutionBuffering --project Nadosh.Infrastructure/Nadosh.Infrastructure.csproj --startup-project Nadosh.Api/Nadosh.Api.csproj --context NadoshDbContext`

## Notes

This implementation intentionally starts with orchestration and safety plumbing rather than direct task execution. The edge runner can now identify itself, report the same approved tool surface across Windows and Linux builds, and receive mothership-managed authorized task metadata without changing the existing worker execution pipeline yet.
