# Agent Assessment Feature Progress

Last updated: 2026-03-12

## What shipped in this change

This change starts the agent assessment foundation in a safe, reusable way:

- Added neutral assessment tool definition models in `Nadosh.Core/Models/AssessmentToolDefinition.cs`
- Added the tool catalog interface in `Nadosh.Core/Interfaces/IAssessmentToolCatalog.cs`
- Added a validated default catalog in `Nadosh.Core/Services/DefaultAssessmentToolCatalog.cs`
- Added an `AssessmentRun` domain model in `Nadosh.Core/Models/AssessmentRun.cs`
- Added assessment policy request/result models in `Nadosh.Core/Models/AssessmentPolicyEvaluation.cs`
- Added assessment submission request/result models in `Nadosh.Core/Models/AssessmentRunSubmission.cs`
- Added assessment run persistence contracts in `Nadosh.Core/Interfaces/IAssessmentRunRepository.cs`
- Added the policy gate contract in `Nadosh.Core/Interfaces/IAssessmentPolicyService.cs`
- Added the run submission contract in `Nadosh.Core/Interfaces/IAssessmentRunService.cs`
- Added the default policy gate implementation in `Nadosh.Core/Services/AssessmentPolicyService.cs`
- Added the run submission/orchestration service in `Nadosh.Core/Services/AssessmentRunService.cs`
- Added the evidence bundle model in `Nadosh.Core/Models/AssessmentEvidenceBundle.cs`
- Added Microsoft Agent Framework adapter context models in `Nadosh.Core/Models/MicrosoftAgentAssessmentContext.cs`
- Added the infrastructure repository in `Nadosh.Infrastructure/Data/AssessmentRunRepository.cs`
- Added the infrastructure evidence builder in `Nadosh.Infrastructure/Data/AssessmentEvidenceService.cs`
- Added the edge task execution tracker implementation in `Nadosh.Infrastructure/Data/EdgeTaskExecutionTracker.cs`
- Wired `AssessmentRun` into `Nadosh.Infrastructure/Data/NadoshDbContext.cs`
- Registered the repository, evidence service, policy service, submission service, and agent adapter in `Nadosh.Infrastructure/InfrastructureServiceCollectionExtensions.cs`
- Registered the catalog for worker-side consumption in `Nadosh.Workers/Program.cs`
- Added a queue-backed assessment worker in `Nadosh.Workers/AssessmentRunWorker.cs`
- Registered the worker under the `assessment-runs` role in `Nadosh.Workers/Program.cs`
- Added the Microsoft Agent Framework adapter interface in `Nadosh.Core/Interfaces/IMicrosoftAgentAssessmentAdapter.cs`
- Added the Microsoft Agent Framework adapter service in `Nadosh.Core/Services/MicrosoftAgentAssessmentAdapter.cs`
- Added API endpoints in `Nadosh.Api/Controllers/AssessmentRunsController.cs` for submit, get by id, list by status, evidence retrieval, and agent-context retrieval
- Added a dedicated core test project in `Nadosh.Core.Tests/`
- Added regression tests in `Nadosh.Core.Tests/Assessment/DefaultAssessmentToolCatalogTests.cs`
- Added policy and run-model tests in `Nadosh.Core.Tests/Assessment/`
- Added submission flow tests in `Nadosh.Core.Tests/Assessment/AssessmentRunServiceTests.cs`
- Added Microsoft Agent Framework adapter tests in `Nadosh.Core.Tests/Assessment/MicrosoftAgentAssessmentAdapterTests.cs`
- Added API integration tests in `Nadosh.Api.Tests/Assessment/AssessmentRunsControllerTests.cs`
- Added a custom API test host in `Nadosh.Api.Tests/Infrastructure/AssessmentApiFactory.cs`

## Tool definitions added

The following approved tool definitions now exist:

- `asset.discovery.reconcile`
- `evidence.bundle.create`
- `exposure.query.current`
- `service.http.metadata.collect`
- `service.tls.certificate.collect`
- `validation.workflow.dry-run`
- `vulnerability.cve.correlate`

## Guardrails currently enforced

The default catalog rejects tool definitions that:

- allow state-changing actions
- allow binary payloads
- allow remote code execution
- omit safety checks
- allow external use without an approval requirement

The assessment policy gate also rejects or pauses requests that:

- omit required request fields
- reference unregistered tools
- use broad wildcard scope such as `0.0.0.0/0` or `*`
- omit required scope tags like `authorized-scope`
- target external environments without an approval reference when the tool requires one

The submission service now:

- evaluates policy before persisting a run
- stores the policy decision JSON on the run record
- sets run status to `Queued`, `AwaitingApproval`, or `Denied`
- writes an audit event for each submitted run

The API surface now provides:

- `POST /v1/AssessmentRuns` to submit a run
- `GET /v1/AssessmentRuns/{runId}` to retrieve a run
- `GET /v1/AssessmentRuns?status=Queued` to review runs by status
- `GET /v1/AssessmentRuns/{runId}/evidence` to build a run evidence bundle
- `GET /v1/AssessmentRuns/{runId}/agent-context` to retrieve a Microsoft Agent Framework-ready run context

The assessment worker now:

- polls `Queued` runs
- marks runs `InProgress` while processing
- completes dry-run and passive runs with evidence-based summaries
- marks active validation runs as `Failed` with an explicit `active-validation-execution-not-implemented` result

The Microsoft Agent Framework adapter now:

- builds a run-scoped session/context document for agent orchestration
- includes safe system instructions, workflow hints, evidence, and approved tool manifests
- marks each tool as `Ready`, `ApprovalRequired`, `EnvironmentBlocked`, `ExecutionAdapterPlanned`, or `Disabled`
- makes current execution limitations explicit instead of leaving them implicit

The API integration coverage now verifies:

- authenticated assessment-run submission via `POST /v1/AssessmentRuns`
- filtered run listing via `GET /v1/AssessmentRuns?status=...`
- evidence retrieval via `GET /v1/AssessmentRuns/{runId}/evidence`
- agent-context retrieval via `GET /v1/AssessmentRuns/{runId}/agent-context`
- stable in-memory test data sharing through a factory-scoped `InMemoryDatabaseRoot`

## Where this fits next

Recommended next implementation slices:

1. Add a real Microsoft Agent Framework runtime host that consumes the generated agent context
2. Add an EF migration for the `AssessmentRuns` table
3. Expand evidence bundling for CIDR and workflow scopes
4. Replace the active-validation placeholder path with approved execution adapters

## Not implemented in this change

This change does **not** add exploit orchestration, payload generation, remote code execution, command-and-control, or Metasploit automation.

## Development notes

- The catalog lives in `Nadosh.Core` so it can be reused by workers, APIs, and future agent adapters.
- The initial tool set mirrors capabilities already present in discovery, enrichment, and rule execution.
- The tests focus on deterministic lookup and safety invariants.
- `AssessmentRun` persistence is wired into the EF model, but a migration for the new table has not been added in this slice.
- Evidence bundling currently supports exact IP/hostname matches well, parses `host:port` service scopes, and uses conservative fallback matching for application/workflow scopes.
- `AssessmentRunWorker` intentionally does not execute active validation actions yet; it completes passive/dry-run work only and records a clear placeholder failure for active validation requests.
- The Microsoft Agent Framework integration in this slice is a runtime-agnostic bridge layer; it does not yet add the preview `Microsoft.Agents.*` packages or start a live model-backed agent host.
- `Nadosh.Api.Tests` now gives the assessment controller an end-to-end safety net; the final stabilization fix was sharing a factory-scoped `InMemoryDatabaseRoot` so seeded runs are visible both to direct seeding contexts and live request handling.
- `Nadosh.Infrastructure.Tests` currently has unrelated pre-existing compile issues from duplicate repository implementations in `Nadosh.Infrastructure`, so the new catalog tests were isolated in `Nadosh.Core.Tests` for clean verification.
