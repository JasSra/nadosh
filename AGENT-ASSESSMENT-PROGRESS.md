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
- Added the infrastructure repository in `Nadosh.Infrastructure/Data/AssessmentRunRepository.cs`
- Wired `AssessmentRun` into `Nadosh.Infrastructure/Data/NadoshDbContext.cs`
- Registered the repository, policy service, and submission service in `Nadosh.Infrastructure/InfrastructureServiceCollectionExtensions.cs`
- Registered the catalog for worker-side consumption in `Nadosh.Workers/Program.cs`
- Added a dedicated core test project in `Nadosh.Core.Tests/`
- Added regression tests in `Nadosh.Core.Tests/Assessment/DefaultAssessmentToolCatalogTests.cs`
- Added policy and run-model tests in `Nadosh.Core.Tests/Assessment/`
- Added submission flow tests in `Nadosh.Core.Tests/Assessment/AssessmentRunServiceTests.cs`

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

## Where this fits next

Recommended next implementation slices:

1. Add an evidence bundle builder over current exposures and enrichment results
2. Add API endpoints for submitting and reviewing approved assessment runs
3. Add a queue-backed worker that picks up `Queued` assessment runs
4. Add Microsoft Agent Framework adapters that consume the catalog and policy layer
5. Add an EF migration for the `AssessmentRuns` table once the current infrastructure build debt is addressed

## Not implemented in this change

This change does **not** add exploit orchestration, payload generation, remote code execution, command-and-control, or Metasploit automation.

## Development notes

- The catalog lives in `Nadosh.Core` so it can be reused by workers, APIs, and future agent adapters.
- The initial tool set mirrors capabilities already present in discovery, enrichment, and rule execution.
- The tests focus on deterministic lookup and safety invariants.
- `AssessmentRun` persistence is wired into the EF model, but a migration for the new table has not been added in this slice.
- `Nadosh.Infrastructure.Tests` currently has unrelated pre-existing compile issues from duplicate repository implementations in `Nadosh.Infrastructure`, so the new catalog tests were isolated in `Nadosh.Core.Tests` for clean verification.
