# Agent Assessment Feature Progress

Last updated: 2026-03-12

## What shipped in this change

This change starts the agent assessment foundation in a safe, reusable way:

- Added neutral assessment tool definition models in `Nadosh.Core/Models/AssessmentToolDefinition.cs`
- Added the tool catalog interface in `Nadosh.Core/Interfaces/IAssessmentToolCatalog.cs`
- Added a validated default catalog in `Nadosh.Core/Services/DefaultAssessmentToolCatalog.cs`
- Registered the catalog for worker-side consumption in `Nadosh.Workers/Program.cs`
- Added a dedicated core test project in `Nadosh.Core.Tests/`
- Added regression tests in `Nadosh.Core.Tests/Assessment/DefaultAssessmentToolCatalogTests.cs`

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

## Where this fits next

Recommended next implementation slices:

1. Add an `AssessmentRun` domain model and persistence
2. Add policy evaluation and scope authorization services
3. Add an evidence bundle builder over current exposures and enrichment results
4. Add API endpoints for submitting and reviewing approved assessment runs
5. Add Microsoft Agent Framework adapters that consume the catalog and policy layer

## Not implemented in this change

This change does **not** add exploit orchestration, payload generation, remote code execution, command-and-control, or Metasploit automation.

## Development notes

- The catalog lives in `Nadosh.Core` so it can be reused by workers, APIs, and future agent adapters.
- The initial tool set mirrors capabilities already present in discovery, enrichment, and rule execution.
- The tests focus on deterministic lookup and safety invariants.
- `Nadosh.Infrastructure.Tests` currently has unrelated pre-existing compile issues from duplicate repository implementations in `Nadosh.Infrastructure`, so the new catalog tests were isolated in `Nadosh.Core.Tests` for clean verification.
