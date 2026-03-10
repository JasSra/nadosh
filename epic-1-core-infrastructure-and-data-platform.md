# Epic 1 — Core Infrastructure & Data Platform

> **Goal:** Establish the foundational data stores, queue infrastructure, and shared libraries that every other component of the exposure intelligence platform depends on.

---

## 1. Epic Overview

This epic delivers the **backbone** of the system: the PostgreSQL database with its schema, the Redis cluster with its key structures, the queue transport layer, and the shared .NET libraries (models, configuration, telemetry plumbing) that all services and workers will consume. Nothing else ships until this foundation is solid.

---

## 2. Deliverables

| # | Deliverable | Description |
|---|-------------|-------------|
| D1.1 | **PostgreSQL schema & migrations** | Complete DDL for all core tables, partitioning strategy, indexes, constraints, and a repeatable migration pipeline. |
| D1.2 | **Redis key schema & provisioning scripts** | Documented key naming conventions, TTL policies, data-structure choices (hashes, sorted sets, streams, sets, counters), and automated provisioning/validation scripts. |
| D1.3 | **Queue transport layer** | A .NET abstraction over Redis-backed queues (or Streams) supporting delayed jobs, priority, retries with back-off, dead-letter, job leasing, visibility timeout, and idempotency keys. |
| D1.4 | **Shared .NET library package(s)** | Common models, DTOs, constants, configuration binding, health-check helpers, and OpenTelemetry bootstrapping used by every service. |
| D1.5 | **Local development environment** | Docker Compose (or equivalent) that brings up Postgres, Redis, and seed data so any developer can run the full stack locally. |
| D1.6 | **Data model documentation** | Entity-relationship diagrams, partitioning rationale, index justification, and capacity estimates. |

---

## 3. High-Level Tasks

### 3.1 PostgreSQL Database Design & Setup

| # | Task | Details |
|---|------|---------|
| T1.1.1 | Design the **Target** table | Columns: `ip`, `cidr_source`, `ownership_tags`, `monitored`, `last_scheduled`, `next_scheduled`. Primary key on IP. Include indexes for scheduling queries. |
| T1.1.2 | Design the **Observation** (history) table | Append-only. Columns: `target_id`, `observed_at`, `port`, `protocol`, `state`, `latency_ms`, `fingerprint`, `evidence` (JSONB), `scan_run_id`. **Partition by month** on `observed_at`. |
| T1.1.3 | Design the **CurrentExposure** table | Small, heavily indexed. Columns: `target_id`, `port`, `protocol`, `current_state`, `first_seen`, `last_seen`, `last_changed`, `classification`, `severity`, `cached_summary`. Composite index on `(ip, port, protocol)`. |
| T1.1.4 | Design the **CertificateObservation** table | Columns: `target_id`, `port`, `sha256`, `subject`, `san_list`, `issuer`, `valid_from`, `valid_to`, `observed_at`. Index on `sha256` and `subject`. |
| T1.1.5 | Design the **EnrichmentResult** table | Columns: `observation_id`, `current_exposure_id`, `rule_id`, `rule_version`, `result_status`, `confidence`, `tags`, `summary`, `evidence` (JSONB), `executed_at`. |
| T1.1.6 | Design the **RuleConfig** table | Columns: `rule_id`, `version`, `service_type`, `trigger_conditions`, `request_definition`, `matcher_definition`, `severity_mapping`, `enabled`, `created_at`, `updated_at`. Versioned rows, not updates-in-place. |
| T1.1.7 | Design the **ScanRun** table | Columns: `run_id`, `stage`, `shard`, `started_at`, `completed_at`, `worker_id`, `counts` (JSONB), `status`. |
| T1.1.8 | Design **audit & suppression tables** | `AuditEvent` for config/admin changes. `SuppressionRule` for excluded targets/ports. |
| T1.1.9 | Implement **time-based partitioning** for Observation & CertificateObservation | Monthly partitions. Automate partition creation via cron or a management script. Validate partition pruning with `EXPLAIN`. |
| T1.1.10 | Create **migration pipeline** | Use a tool such as FluentMigrator, DbUp, or EF Core migrations. Ensure migrations are idempotent and versioned in source control. |
| T1.1.11 | Write **capacity estimates** | Document row-size estimates, projected table sizes at 56 M targets × 15-day cycles, and storage growth per month. |

### 3.2 Redis Infrastructure Design & Setup

| # | Task | Details |
|---|------|---------|
| T1.2.1 | Define **key naming conventions** | Prefix scheme: `scan:schedule:{shard}`, `job:lease:{jobId}`, `target:last:{ip}`, `exposure:current:{ip}`, `querycache:{hash}`, `dedupe:stage1:{target}:{port}:{window}`, `dedupe:stage2:{obs}:{ruleVersion}`. Document in the shared library. |
| T1.2.2 | Define **data-structure choices** per use case | Sorted sets for scheduling, hashes for current-state cache, streams/lists for queues, sets for dedup, strings/counters for rate limits. |
| T1.2.3 | Define **TTL & eviction policies** | Per-key-class TTLs. Cache keys: configurable (default 5 min). Dedup keys: scan-window-aligned. Lease keys: heartbeat-driven. Document eviction strategy (`allkeys-lfu` or `volatile-ttl`). |
| T1.2.4 | Create **provisioning/validation scripts** | Startup scripts that verify Redis connectivity, create consumer groups for streams, and validate key-space health. |
| T1.2.5 | Design **distributed lock strategy** | Where locks are needed (scheduler shard claims, config activation), define Redlock or single-instance lock patterns with TTLs. |

### 3.3 Queue Transport Layer

| # | Task | Details |
|---|------|---------|
| T1.3.1 | Define **IJobQueue<T>** abstraction | Interface covering: `EnqueueAsync`, `EnqueueDelayedAsync`, `DequeueAsync`, `AcknowledgeAsync`, `RejectAsync`, `DeadLetterAsync`. Support priority levels and idempotency keys. |
| T1.3.2 | Implement **Redis-backed queue** | Implement using Redis Streams (with consumer groups) or sorted-set + list pattern. Support visibility timeout via lease keys. |
| T1.3.3 | Implement **retry with exponential back-off** | Track attempt count. Re-enqueue with increasing delay. Move to dead-letter after max attempts (configurable per queue). |
| T1.3.4 | Implement **dead-letter queue** | Separate stream/list per source queue. Include original payload, error info, attempt count, timestamps. |
| T1.3.5 | Implement **job lease / heartbeat** | Workers renew leases on a timer. If lease expires, the job becomes visible to other consumers. |
| T1.3.6 | Implement **shard-aware dispatch** | Queues partitioned by shard key. Workers can subscribe to specific shards or all shards. |
| T1.3.7 | Write **integration tests** | Test enqueue/dequeue, retry, dead-letter, lease expiry, duplicate delivery idempotency, and priority ordering. |

### 3.4 Shared .NET Libraries

| # | Task | Details |
|---|------|---------|
| T1.4.1 | Create **domain model** package | Shared C# models/records for `Target`, `Observation`, `CurrentExposure`, `CertificateObservation`, `EnrichmentResult`, `RuleConfig`, `ScanRun`, `AuditEvent`. |
| T1.4.2 | Create **configuration binding helpers** | Strongly-typed options for database connection strings, Redis endpoints, queue names, scan policies, TTLs, feature flags. |
| T1.4.3 | Create **OpenTelemetry bootstrap** | Shared extension method that wires up structured logging (Serilog or Microsoft.Extensions.Logging), metrics (Prometheus/OTLP), and distributed tracing for all services. |
| T1.4.4 | Create **health-check helpers** | `IHealthCheck` implementations for Postgres connectivity, Redis connectivity, queue depth thresholds. |
| T1.4.5 | Create **repository interfaces** | `ITargetRepository`, `IObservationRepository`, `ICurrentExposureRepository`, `ICertificateRepository`, `IRuleConfigRepository`. Define contracts; implementations live in the data-access layer. |
| T1.4.6 | Create **data-access layer** | Dapper or EF Core implementations of the repository interfaces. Include bulk-upsert helpers for batch ingestion. |

### 3.5 Local Development Environment

| # | Task | Details |
|---|------|---------|
| T1.5.1 | Author **Docker Compose** file | Services: Postgres (with init script), Redis, optional pgAdmin, optional RedisInsight. Expose standard ports. |
| T1.5.2 | Create **seed data scripts** | Generate a small but representative target inventory (e.g., 10 K IPs), sample observations, sample current-exposure rows, sample rules. |
| T1.5.3 | Write a **README / Getting Started** guide | Steps to clone, `docker compose up`, run migrations, run seed, start any service. |

---

## 4. Acceptance Criteria

1. A fresh `docker compose up` followed by `dotnet run` of any service connects to Postgres and Redis without manual intervention.
2. Migrations produce all tables, indexes, partitions, and constraints described above.
3. The queue library passes integration tests covering enqueue, dequeue, retry, dead-letter, lease expiry, and idempotent re-delivery.
4. Shared models compile and are referenced by at least one consuming service project.
5. OpenTelemetry traces and metrics appear in a local collector/exporter (e.g., Jaeger or console exporter).
6. Capacity estimate document covers storage projections for at least 12 months of operation at target scale.

---

## 5. Dependencies & Risks

| Risk | Mitigation |
|------|------------|
| Partitioning strategy chosen too early without real data patterns | Start with monthly partitions; re-evaluate after MVP load tests. |
| Redis memory sizing unknown | Document estimated key sizes; run memory-usage checks against seed data; plan for eviction. |
| Queue abstraction may not cover all future needs | Design interface for extension; avoid leaking Redis internals into consumer code. |
| Migration tool choice affects team velocity | Pick a tool the team already knows; document rollback procedure. |

---

## 6. Estimated Scope

| Category | Rough Effort |
|----------|-------------|
| Database schema + migrations | Medium |
| Redis design + scripts | Small–Medium |
| Queue transport library | Medium–Large |
| Shared .NET libraries | Medium |
| Local dev environment | Small |
| Documentation & diagrams | Small |

---

*This epic has no external user-facing surface. Its sole consumer is the rest of the platform. It must be complete (or at least its interfaces must be stable) before Epics 2, 3, and 4 begin active development.*
