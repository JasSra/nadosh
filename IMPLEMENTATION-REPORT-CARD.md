# Nadosh Implementation Report Card

Last updated: 2026-03-11

This file is the living status tracker for closing the gap between the current repository state and the goals defined in:

- `epic-1-core-infrastructure-and-data-platform.md`
- `epic-2-scanning-engine-and-enrichment-pipeline.md`
- `epic-3-read-only-api-and-query-layer.md`
- `epic-4-security-observability-and-operations.md`

## Current grades

- **Epic 1 — B+**  
  Foundation exists, and queue reliability/configurability are materially stronger now. DB, Redis, migrations, models, compose, and a tested queue abstraction exist; shard-aware dispatch is now implemented and validated, though some documentation/design/operability work is still incomplete.

- **Epic 2 — B**  
  Pipeline works end-to-end, but Stage 2 config/execution is incomplete. Discovery → Banner → Fingerprint → Classify is real; declarative rule execution and the persisted state machine remain partial.

- **Epic 3 — B-**  
  API works, but the full query contract is incomplete. Core endpoints exist; cursor pagination, richer DSL/query model, and several endpoints are still missing.

- **Epic 4 — C**  
  Basic auth/rate limiting exists, but production auth, RBAC, dashboards, alerts, suppression workflow, and real audit usage are not complete.

## Active remediation slices

- ✅ **Report card + execution tracking**  
  Keep an honest running view of progress and remaining gaps.

- ✅ **RuleConfig registry**  
  Replace Stage 2 hardcoded assumptions with versioned active rule lookup.

- ✅ **Stage 2 rule resolution**  
  Use active `RuleConfig` rows and persist the actual rule version executed.

- ✅ **Initial rule pack seeding**  
  Ensure TLS / HTTP / SSH / RDP rules exist in the DB for local/dev use.

- ✅ **Minimal Stage 2 executor path**  
  Execute live TLS certificate and HTTP metadata collection, and persist real per-rule outcomes.

- ✅ **Classifier-to-rule alignment**  
  Ensure Stage 2 jobs only enqueue seeded, resolvable rule IDs.

- ✅ **Observation pipeline state machine (initial slice)**  
  Persist and enforce workflow state on `Observation` across the active fingerprint → classifier → Stage 2 path.

- ✅ **Pre-observation dispatch tracking (initial slice)**  
  Persist scheduler/discovery workflow state per target+batch before observation-level identity exists.

- ✅ **Banner/fingerprint handoff dispatch tracking (initial slice)**  
  Persist banner and fingerprint queue handoffs per source observation with duplicate-safe claim/complete/error semantics.

- ✅ **Classification handoff dispatch tracking (initial slice)**  
  Extend the generic observation handoff flow to the classification queue so the final pre-classifier hop is persisted and resumable.

- ✅ **Stage 2 queue dispatch + retry orchestration (initial slice)**  
  Extend the generic observation handoff flow to Stage 2 and requeue failed enrichment attempts with explicit state re-entry instead of dead-lettering immediately.

- ✅ **Redis queue transport hardening (initial slice)**  
  Fix processing-entry identity bugs in the Redis queue implementation so ack/reject/dead-letter remove the correct item, delayed jobs become visible, and malformed dequeues do not strand processing entries.

- ✅ **Redis queue lease/idempotency/priority hardening (initial slice)**  
  Add lease-expiry recovery, enforce enqueue-time idempotency keys, honor the existing queue priority parameter with initial high/normal/low semantics, and back the queue behavior with focused Redis integration tests.

- ✅ **Queue heartbeat + retry backoff wiring (initial slice)**  
  Add worker-side lease renewal during long-running job processing, support delayed re-enqueue in the queue transport, and apply exponential retry backoff semantics across the active worker fleet.

- ✅ **Queue policy configuration helpers (initial slice)**  
  Add strongly typed queue transport settings, resolve per-queue policy at runtime, and drive visibility timeout / retry / idempotency behavior from configuration instead of hardcoded constants.

- ✅ **Shard-aware queue dispatch (initial slice)**  
  Route jobs to shard-local ready/delayed/processing/dead-letter Redis keys, let workers consume subscribed shard subsets, and keep producers shard-stable by target identity.

## What changed in this pass

- Added this report card as the live source of truth for gap closure.
- Began converting Stage 2 from hardcoded/mock rule handling toward a real `RuleConfig`-backed path.
- Added a dedicated `IRuleExecutionService` for Stage 2 rule execution.
- Implemented live TLS certificate collection for `tls-cert-check`.
- Implemented live HTTP title/header collection for `http-title-check`.
- Updated `Stage2Worker` to persist executor outcomes instead of synthetic summaries/evidence.
- Updated `ClassifierWorker` to enqueue only seeded rule IDs: `tls-cert-check`, `http-title-check`, `ssh-banner-check`, and `rdp-presence-check`.
- Added persisted pipeline state fields to `Observation` (`PipelineState`, timestamp, worker ID, retry count).
- Added `IObservationPipelineStateService` with valid transition enforcement and rejected-transition audit logging.
- Instrumented the active worker path to persist: `Stage1Observed → Classified → FlaggedForEnrichment → Stage2Queued → Stage2Processing → Enriched → Completed`, plus `Error` on Stage 2 and enqueue failures.
- Added the EF migration `20260311065729_AddObservationPipelineState`.
- Added `Stage1Dispatch` as a per-target, per-batch pre-observation workflow record keyed by `(BatchId, TargetIp)`.
- Added `IStage1DispatchStateService` to enforce and persist `Scheduled → Stage1Scanning → Stage1Observed/Error` transitions with duplicate-delivery handling.
- Instrumented `SchedulerService` to create dispatch rows before Stage 1 enqueue and mark failures when queueing breaks.
- Instrumented `DiscoveryWorker` to claim dispatch rows on dequeue, skip duplicate/redelivered work safely, and mark observation persistence completion.
- Added the EF migration `20260311071712_AddStage1DispatchTracking`.
- Added `ObservationHandoffDispatch` as a generic per-source-observation queue handoff record for `BannerGrab` and `Fingerprint` stages.
- Added `IObservationHandoffDispatchService` with queue-style `Queued → Processing → Completed/Error` semantics and rejected-transition audit logging.
- Instrumented `DiscoveryWorker` to create banner handoff rows before enqueueing `BannerGrabJob` and record enqueue failures.
- Instrumented `BannerGrabWorker` and `FingerprintWorker` to claim handoff rows on dequeue, reuse already-produced observations on retry, and complete/fail handoff rows around downstream queueing.
- Added the EF migration `20260311073553_AddObservationHandoffDispatchTracking`.
- Extended `ObservationHandoffDispatchKind` to include `Classification` without changing the table schema.
- Instrumented `FingerprintWorker` and the legacy `Stage1Worker` to schedule classification handoff rows before enqueueing `ClassificationJob` and mark enqueue failures cleanly.
- Instrumented `ClassifierWorker` to claim, complete, and fail classification handoff rows so duplicate delivery and resume behavior are explicit for the last classification queue hop.
- Verified this slice required **no EF migration**, because the existing table already stores dispatch kind as a string and the schema shape did not change.
- Extended `ObservationHandoffDispatchKind` again to include `Stage2Enrichment`, also without changing the table schema.
- Instrumented `ClassifierWorker` to schedule Stage 2 handoff rows before enqueueing `Stage2EnrichmentJob` and avoid duplicate Stage 2 publish when a handoff already exists.
- Instrumented `Stage2Worker` to claim, complete, fail, and reopen Stage 2 handoff rows, tying the enrichment queue hop into the same persisted handoff model as earlier stages.
- Replaced first-failure dead-letter behavior in `Stage2Worker` with retry-aware handling: attempts 1-2 move through `Error -> Stage2Queued` re-entry and requeue, while attempt 3 dead-letters.
- Verified this slice also required **no EF migration**, because only new dispatch-kind values and worker behavior changed.
- Added transport-only raw processing payload tracking to `JobQueueMessage<T>` so the Redis queue can later remove the exact JSON entry that was moved into `:processing`.
- Fixed `RedisJobQueue<T>` so `AcknowledgeAsync`, `RejectAsync`, and `DeadLetterAsync` remove the original processing entry instead of serializing a mutated message and silently failing to remove anything.
- Added due delayed-job promotion in `DequeueAsync`, so payloads written by `EnqueueDelayedAsync` are now actually eligible for consumption.
- Added malformed-payload cleanup and dead-letter routing so undecodable items do not get stranded forever in the processing list.
- Added richer dead-letter entries with queue/error/timestamp context for queue-managed dead-letter writes.
- Verified this slice required **no EF migration**, because the queue hardening only changed transport behavior and transient job metadata.
- Added `JobQueueMessage<T>.IdempotencyKey` and `JobQueueMessage<T>.Priority` so queue-managed transport metadata survives requeue, dead-letter, and delayed promotion paths.
- Namespaced lease keys by queue, validated lease-token ownership before ack/reject/dead-letter operations, and added dequeue-time recovery of expired processing entries so abandoned jobs become visible again instead of remaining stranded forever.
- Implemented actual enqueue-time idempotency enforcement with a Redis transaction and a bounded queue-local dedupe window, so repeated producer submissions with the same idempotency key collapse to one queued job.
- Extended `EnqueueDelayedAsync` with a backward-compatible optional `priority` parameter and added initial three-band priority routing (`high`, `normal`, `low`) for ready/dequeued/recovered jobs.
- Added `Nadosh.Infrastructure.Tests` and wired it into `Nadosh.slnx`.
- Added focused Redis queue integration tests covering: enqueue/dequeue/ack, reject+reenqueue attempt tracking, delayed-job promotion, malformed payload dead-lettering, lease-expiry recovery, idempotency suppression, and priority ordering.
- Verified this slice also required **no EF migration**, because the queue reliability work only changed transport semantics and test coverage.
- Extended `IJobQueue<T>.RejectAsync` with a backward-compatible optional `reenqueueDelay` so workers can requeue failed jobs with an explicit delay instead of immediately pushing them back onto the ready queue.
- Updated `RedisJobQueue<T>` to honor delayed rejects by writing re-enqueued jobs into the delayed sorted set, preserving the same transport envelope, attempt count, priority, and idempotency metadata.
- Added `QueueProcessingUtilities` in `Nadosh.Workers` to renew leases periodically while a worker is actively processing a job and to centralize exponential retry backoff/dead-letter decisions.
- Wired the lease-heartbeat helper into the active queue consumers: `DiscoveryWorker`, `BannerGrabWorker`, `FingerprintWorker`, `ClassifierWorker`, `Stage1Worker`, `Stage2Worker`, and `MacEnrichmentWorker`.
- Replaced immediate retry loops in the active workers with exponential backoff delays, while preserving the existing max-attempt dead-letter behavior.
- Expanded the Redis queue integration suite to cover delayed reject/backoff behavior and lease renewal preventing premature recovery.
- Verified this slice also required **no EF migration**, because the changes were limited to queue semantics, worker processing behavior, and integration tests.
- Added shared queue configuration models in `Nadosh.Core` (`QueueTransportOptions`, `QueuePolicyOptions`, `ResolvedQueuePolicy`) plus `IQueuePolicyProvider` so queue behavior can be resolved per job type without leaking Redis internals into worker code.
- Added `QueuePolicyProvider` in `Nadosh.Infrastructure` and bound the `QueueTransport` configuration section through DI, including a Redis connection-string fallback to `Redis:ConnectionString` when `ConnectionStrings:Redis` is absent.
- Updated `RedisJobQueue<T>` to consume per-queue idempotency-window policy rather than a hardcoded one-hour constant.
- Updated the active queue-consuming workers to resolve per-queue policies and use them for dequeue visibility timeout, lease heartbeat cadence, retry backoff, and max-attempt handling.
- Added `QueueTransport` configuration examples to `Nadosh.Workers/appsettings.json` for default queue policy plus explicit overrides for Stage 1, classification, and Stage 2 jobs.
- Added focused tests for policy resolution and for a custom short idempotency window expiring and allowing re-enqueue.
- Verified this slice also required **no EF migration**, because the changes were limited to configuration binding, queue behavior, worker wiring, and tests.
- Extended `IJobQueue<T>` with `JobEnqueueOptions` and shard metadata so producers can pass stable shard keys without breaking existing callers.
- Extended queue policy resolution to include per-queue `ShardCount` plus worker-level `SubscribedShards`, with normalization/fallback behavior for single-shard and multi-shard setups.
- Reworked `RedisJobQueue<T>` to use shard-local ready, delayed, processing, and dead-letter Redis keys while preserving existing non-sharded key names when `ShardCount = 1`.
- Updated dequeue, delayed-promotion, lease-recovery, reject, and dead-letter paths to preserve shard identity end-to-end.
- Updated the active queue producers (`SchedulerService`, `DiscoveryWorker`, `BannerGrabWorker`, `FingerprintWorker`, `Stage1Worker`, `ClassifierWorker`, and `ArpScanner`) to shard by stable target identity so all jobs for the same target flow through the same queue partition.
- Added focused shard-aware Redis integration tests for subscribed-shard dequeue filtering and shard-local delayed-job promotion.
- Added shard-related configuration examples to `Nadosh.Workers/appsettings.json` via `SubscribedShards` and `Default.ShardCount`.
- Verified this slice also required **no EF migration**, because the changes were limited to queue routing behavior, worker publish semantics, configuration binding, and transport tests.

## Still open after this pass

- Full multi-stage state machine coverage across all queue handoffs and worker stages
- Broader Stage 2 rule execution coverage (SSH/RDP still use observation-backed fallback evidence)
- Retry re-entry is now wired for the Stage 2 queue hop, but broader cross-stage retry orchestration and suppression transitions remain incomplete.
- Cursor pagination and broader API contract work
- Production-grade security/ops features

## Verification log

- 2026-03-11 — Full solution build before remediation pass — **Passed**
- 2026-03-11 — Epic gap assessment against repo state — **Completed**
- 2026-03-11 — Full solution build after RuleConfig registry and Stage 2 wiring changes — **Passed**
- 2026-03-11 — Full solution build after Stage 2 executor and classifier rule alignment — **Passed**
- 2026-03-11 — EF migration generation for observation pipeline state — **Passed**
- 2026-03-11 — Full solution build after initial observation state-machine slice — **Passed**
- 2026-03-11 — EF migration generation for Stage 1 dispatch tracking — **Passed**
- 2026-03-11 — Full solution build after pre-observation dispatch tracking slice — **Passed**
- 2026-03-11 — EF migration generation for observation handoff dispatch tracking — **Passed**
- 2026-03-11 — Full solution build after banner/fingerprint handoff tracking slice — **Passed**
- 2026-03-11 — Full solution build after classification handoff tracking slice — **Passed**
- 2026-03-11 — Full solution build after Stage 2 queue dispatch + retry slice — **Passed**
- 2026-03-11 — Full solution build after Redis queue transport hardening slice — **Passed**
- 2026-03-11 — Redis queue integration test suite (`Nadosh.Infrastructure.Tests`) — **Passed**
- 2026-03-11 — Full solution build after Redis queue lease/idempotency/priority slice — **Passed**
- 2026-03-11 — Expanded Redis queue integration test suite after heartbeat/backoff slice — **Passed**
- 2026-03-11 — Full solution build after queue heartbeat/backoff slice — **Passed**
- 2026-03-11 — Queue policy/provider tests + expanded Redis queue suite after configuration slice — **Passed**
- 2026-03-11 — Full solution build after queue policy configuration slice — **Passed**
- 2026-03-11 — Shard-aware Redis queue integration tests (`Nadosh.Infrastructure.Tests`) — **Passed**
- 2026-03-11 — Full solution build after shard-aware dispatch slice — **Passed**

## Next recommended slice

With shard-aware dispatch now in place, the next highest-value workflow gap is broader end-to-end state-machine coverage: finish persisted dispatch/state transitions for the remaining worker paths, strengthen retry/suppression semantics across non-Stage-2 hops, and then widen Stage 2 rule execution coverage beyond the current TLS/HTTP-heavy slice.
