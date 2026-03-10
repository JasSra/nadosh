# Epic 2 — Scanning Engine & Enrichment Pipeline

> **Goal:** Deliver the end-to-end scanning pipeline — from scheduling through Stage 1 broad discovery, classification, Stage 2 enrichment, and result persistence — including the declarative rule/config system that makes enrichment extensible without code changes.

---

## 1. Epic Overview

This is the **core engine** of the platform. It takes the target inventory, decides what to scan and when, performs lightweight internet-wide probes (Stage 1), classifies results, flags interesting findings for deeper inspection, runs config-driven enrichment checks (Stage 2), and writes all observations to both the durable historical store and the hot cache. Every component is queue-driven, stateless, idempotent, and horizontally scalable.

---

## 2. Deliverables

| # | Deliverable | Description |
|---|-------------|-------------|
| D2.1 | **Scheduler service** | A .NET hosted service that evaluates the target inventory, determines next-due targets, slices work into batches/shards, and enqueues Stage 1 jobs. |
| D2.2 | **Stage 1 scan workers** | Stateless workers that dequeue Stage 1 jobs, probe targets on configured common ports, collect banners/TLS/fingerprints, and publish raw observations. |
| D2.3 | **Classifier worker** | Worker that normalises Stage 1 raw results into the common service model and flags candidates for Stage 2 enrichment. |
| D2.4 | **Stage 2 enrichment workers** | Workers that dequeue enrichment jobs and execute declarative, config-driven service-specific checks against flagged targets. |
| D2.5 | **Declarative rule/config system** | A config registry, validation pipeline, versioned storage, and runtime rule engine that drives all Stage 2 checks. |
| D2.6 | **Cache projector worker** | Worker that updates Redis hot views (current-exposure cache, recent-state hashes) after observations are persisted. |
| D2.7 | **Compaction / materialisation worker** | Worker that rolls append-only observation history into the `CurrentExposure` table and produces aggregates. |
| D2.8 | **Multi-stage state machine** | Persisted state tracking for every target/finding through the pipeline: `Scheduled → Stage1Scanning → Stage1Observed → Classified → FlaggedForEnrichment → Stage2Queued → Stage2Processing → Enriched → Completed` (plus `Suppressed`, `Error`). |
| D2.9 | **Target inventory import** | CLI tool or admin endpoint to bulk-import IP ranges / CIDR blocks into the target table. |
| D2.10 | **Initial rule pack (2–4 rules)** | MVP declarative checks: TLS certificate collector, HTTP header/title collector, SSH banner parser, RDP presence checker. |

---

## 3. High-Level Tasks

### 3.1 Scheduler Service

| # | Task | Details |
|---|------|---------|
| T2.1.1 | Implement **next-due target query** | Query targets where `next_scheduled <= now()`. Support shard-aware batching (e.g., by IP range hash). Use Redis sorted set `scan:schedule:{shard}` for fast due-check. |
| T2.1.2 | Implement **batch slicing** | Slice due targets into configurable batch sizes (e.g., 1,000 IPs per job). Each batch becomes one Stage 1 queue message. |
| T2.1.3 | Implement **priority-based scheduling** | Priority order: (1) monitored/interesting targets, (2) recently changed targets, (3) high-risk service signatures, (4) background sweep. |
| T2.1.4 | Implement **dynamic cadence adjustment** | Shorter revisit intervals for recently changed hosts, high-risk services, new discoveries. Longer intervals for stable, low-interest targets. Default: 15-day cycle. |
| T2.1.5 | Implement **exclusion / suppression support** | Respect suppression rules. Skip targets/ports on the suppression list. Log suppressed items for audit. |
| T2.1.6 | Implement **cost-aware throttling** | Track enqueue rate. Respect per-shard and global rate limits stored in Redis counters. Avoid flooding queues during burst scheduling. |
| T2.1.7 | Implement **crash-safe resume** | On restart, the scheduler must resume without re-enqueuing already-queued batches. Use dedup keys (`dedupe:stage1:{target}:{port}:{window}`) to prevent duplicate floods. |
| T2.1.8 | Add **structured telemetry** | Metrics: targets due, targets enqueued, batches created, suppressed count, scheduling latency. Traces: per-scheduling-cycle span. |

### 3.2 Stage 1 Scan Workers

| # | Task | Details |
|---|------|---------|
| T2.2.1 | Implement **job dequeue and lease renewal** | Consume from Stage 1 queue. Renew lease on a background timer. Honour graceful shutdown signals. |
| T2.2.2 | Implement **port probing engine** | For each target in the batch, probe the configured common ports list (e.g., 10 ports). Use async socket connections with strict timeout budgets. |
| T2.2.3 | Implement **TLS certificate collection** | On TLS-capable ports (443, 8443, etc.), perform a TLS handshake and extract: subject, SAN list, issuer, validity dates, SHA-256 fingerprint. |
| T2.2.4 | Implement **banner / fingerprint collection** | Collect service banners where safe and cheap: HTTP `Server` header + `<title>`, SSH banner, SMTP banner. Compute fingerprint confidence score. |
| T2.2.5 | Implement **result packaging** | For each probe, produce an `Observation` record with: IP, port, protocol, timestamp, state (open/closed/filtered), latency, fingerprint, evidence blob, scan_run_id. |
| T2.2.6 | Implement **batch result publishing** | Publish observation batch to the classification queue. Also write raw observations to Postgres in bulk upsert. |
| T2.2.7 | Implement **timeout and error handling** | Per-probe timeout (configurable, e.g., 5 s). Per-batch timeout. On partial failure, publish successful results and re-enqueue or dead-letter failures. |
| T2.2.8 | Implement **idempotency** | Use dedup keys to ensure re-delivery of the same batch does not produce duplicate observations. |
| T2.2.9 | Add **structured telemetry** | Metrics: probes attempted, probes succeeded, probes timed out, probes errored, batch duration, TLS certs collected. |

### 3.3 Classifier Worker

| # | Task | Details |
|---|------|---------|
| T2.3.1 | Implement **service classification logic** | Map (port + protocol + banner + fingerprint) → canonical service type (e.g., `http`, `https`, `ssh`, `rdp`, `smtp`, `rtsp`, `snmp`, `unknown`). |
| T2.3.2 | Implement **Stage 2 flagging rules** | Determine which observations should trigger enrichment based on: known port/service signature, certificate/domain interest patterns, host state change, explicit rule triggers. |
| T2.3.3 | Implement **CurrentExposure upsert** | Update or insert into `CurrentExposure` table: set `current_state`, `last_seen`, `last_changed`, `classification`, `severity`. |
| T2.3.4 | Implement **state machine transition** | Transition target/finding state: `Stage1Observed → Classified`. If flagged: `Classified → FlaggedForEnrichment`. |
| T2.3.5 | Enqueue **Stage 2 jobs** for flagged results | Publish enrichment job messages with: target info, observation reference, matched rule IDs, priority. |
| T2.3.6 | Add **structured telemetry** | Metrics: observations classified, services detected by type, enrichment flags raised, classification latency. |

### 3.4 Stage 2 Enrichment Workers

| # | Task | Details |
|---|------|---------|
| T2.4.1 | Implement **job dequeue with rule resolution** | Consume from Stage 2 queue. Resolve the applicable `RuleConfig` by rule_id and current version from the config registry. |
| T2.4.2 | Implement **declarative check executor** | Generic engine that interprets a rule config to: (1) construct a protocol-specific request, (2) send it with timeout, (3) match the response against matchers, (4) extract evidence, (5) map to severity. |
| T2.4.3 | Implement **per-rule concurrency limits** | Enforce maximum concurrent executions per rule type using Redis counters or semaphores. |
| T2.4.4 | Implement **per-rule cost scoring** | Each rule has a cost score. Workers track cumulative cost. Cost-aware scheduling can throttle expensive rules. |
| T2.4.5 | Implement **result persistence** | Write `EnrichmentResult` to Postgres: rule_id, rule_version, result_status, confidence, tags, summary, evidence blob, executed_at. |
| T2.4.6 | Implement **timeout and sandboxing** | Strict per-check timeout. Isolate each check execution. Do not let one slow check block the worker. |
| T2.4.7 | Implement **state machine transition** | `Stage2Processing → Enriched` on success. `Stage2Processing → Error` on failure (with retry logic). |
| T2.4.8 | Implement **next rescan interval recommendation** | Enrichment results can suggest a shorter or longer next rescan interval, feeding back to the scheduler. |
| T2.4.9 | Add **structured telemetry** | Metrics: enrichments attempted, succeeded, failed, timed out, by rule type, rule cost consumed, enrichment latency. |

### 3.5 Declarative Rule / Config System

| # | Task | Details |
|---|------|---------|
| T2.5.1 | Define **rule config schema** | JSON/YAML schema covering: identity, version, trigger conditions, input requirements, protocol type, request pattern, response matcher, evidence extraction, severity mapping, timeout/retry policy, cache TTL, output schema. |
| T2.5.2 | Implement **config registry** | Service/repository that stores, retrieves, and lists rule configs from Postgres (`RuleConfig` table). Support listing by service type, enabled/disabled, version. |
| T2.5.3 | Implement **config validation pipeline** | Validate rule configs against schema before activation. Check: required fields, valid protocol type, valid matcher syntax, valid severity values, cost score present, output schema valid. |
| T2.5.4 | Implement **versioned rollout** | New rule versions are inserted (not updated). Activation flips the `enabled` flag on the new version and disables the old one. Support rollback by re-enabling a prior version. |
| T2.5.5 | Implement **config change audit** | Every activation, deactivation, and rollback is logged to the `AuditEvent` table with actor, timestamp, old version, new version. |
| T2.5.6 | Implement **capability sandboxing** | Rules can only access the protocol/capability their type permits (e.g., an HTTP rule cannot open raw TCP sockets). Enforce at the executor level. |
| T2.5.7 | Author **initial MVP rules** | TLS certificate collector, HTTP header/title collector, SSH banner parser, RDP presence checker. All expressed as declarative configs. |

### 3.6 Cache Projector Worker

| # | Task | Details |
|---|------|---------|
| T2.6.1 | Consume **observation/classification events** | Listen for new current-exposure writes (via queue message or change event). |
| T2.6.2 | Update **Redis current-exposure hash** | Write/update `exposure:current:{ip}` hash with latest port/service/severity data. Set appropriate TTL. |
| T2.6.3 | Update **Redis target-last hash** | Write `target:last:{ip}` with last-seen timestamp and summary. |
| T2.6.4 | Invalidate **query caches** | Event-driven invalidation of `querycache:{hash}` entries affected by the updated target. |
| T2.6.5 | Add **structured telemetry** | Metrics: cache updates written, invalidations triggered, projection latency, Redis write errors. |

### 3.7 Compaction / Materialisation Worker

| # | Task | Details |
|---|------|---------|
| T2.7.1 | Implement **current-state materialisation** | Periodically (or event-driven) scan new observations and ensure `CurrentExposure` is up to date with `first_seen`, `last_seen`, `last_changed`. |
| T2.7.2 | Implement **rollup aggregations** | Produce summary aggregations: open port counts, service-type distributions, severity distributions, per-CIDR summaries. Store in rollup tables or materialised views. |
| T2.7.3 | Implement **stale-target detection** | Identify targets not seen for > N cycles. Update state accordingly. |
| T2.7.4 | Add **structured telemetry** | Metrics: materialisation runs, rows processed, rollups generated, compaction duration. |

### 3.8 Multi-Stage State Machine

| # | Task | Details |
|---|------|---------|
| T2.8.1 | Define **state enum and transitions** | States: `Scheduled`, `Stage1Scanning`, `Stage1Observed`, `Classified`, `FlaggedForEnrichment`, `Stage2Queued`, `Stage2Processing`, `Enriched`, `Suppressed`, `Error`, `Completed`. Define valid transitions. |
| T2.8.2 | Persist **state in database** | Column on `CurrentExposure` or a dedicated state-tracking table. Record state, transition timestamp, worker_id. |
| T2.8.3 | Enforce **valid transitions only** | Reject invalid state transitions. Log violations. |
| T2.8.4 | Support **Error → retry transitions** | On retry, move from `Error` back to the appropriate queue state. Track retry count. |

### 3.9 Target Inventory Import

| # | Task | Details |
|---|------|---------|
| T2.9.1 | Implement **bulk import CLI/endpoint** | Accept CSV or line-delimited CIDR blocks. Expand to individual IPs or store as ranges depending on design. Insert into `Target` table. |
| T2.9.2 | Implement **deduplication on import** | Skip already-known targets. Update metadata (tags, monitored flag) for existing targets. |
| T2.9.3 | Implement **import audit** | Log import source, count, timestamp to `AuditEvent`. |

---

## 4. Acceptance Criteria

1. The scheduler enqueues the correct number of batches for a target set of 10 K IPs with a 15-day cycle.
2. Stage 1 workers probe all configured ports for a batch and produce valid observation records.
3. TLS certificates are collected and persisted for TLS-capable ports.
4. The classifier correctly maps known port/banner combinations to canonical service types.
5. Flagged observations appear in the Stage 2 queue with the correct rule IDs.
6. Stage 2 workers execute the 4 MVP declarative rules and persist enrichment results.
7. A new rule config can be authored, validated, activated, and executed **without any code deployment**.
8. The state machine correctly tracks a target through all stages and rejects invalid transitions.
9. Redis hot cache reflects current-exposure data within seconds of persistence.
10. All workers survive duplicate delivery, process crashes, and lease expiry without data corruption.
11. Dead-letter queues capture all permanently failed jobs with full diagnostic context.

---

## 5. Dependencies

| Dependency | Source |
|------------|--------|
| PostgreSQL schema, migrations, and repository interfaces | Epic 1 |
| Redis key schema and provisioning | Epic 1 |
| Queue transport library (IJobQueue) | Epic 1 |
| Shared .NET models and telemetry bootstrap | Epic 1 |

---

## 6. Risks

| Risk | Mitigation |
|------|------------|
| Scan probes may be rate-limited or blocked by upstream networks | Implement configurable per-region throttles; support proxy/source-IP rotation later. |
| Declarative rule engine may not cover all protocol nuances | Allow escape-hatch "plugin" rules for truly custom logic; keep the interface standard. |
| State machine complexity may slow development | Start with a simple linear pipeline; add branching/suppression states incrementally. |
| TLS handshake failures at scale may overwhelm workers | Strict per-probe timeouts; circuit-breaker pattern for consistently failing targets. |

---

## 7. Estimated Scope

| Category | Rough Effort |
|----------|-------------|
| Scheduler service | Medium |
| Stage 1 scan workers | Large |
| Classifier worker | Medium |
| Stage 2 enrichment workers | Large |
| Declarative rule/config system | Large |
| Cache projector worker | Small–Medium |
| Compaction / materialisation worker | Medium |
| State machine | Medium |
| Target inventory import | Small |
| Initial rule pack (4 rules) | Medium |

---

*This epic is the **heart** of the platform. It depends on Epic 1 being stable. Epics 3 and 4 can proceed in parallel once the data is flowing.*
