# Epic 4 — Security, Observability & Operations

> **Goal:** Harden the platform for production operation by implementing authentication, authorisation, rate limiting, end-to-end observability, operational dashboards, failure-handling infrastructure, and compliance guardrails — so the system is safe, auditable, and operable at scale.

---

## 1. Epic Overview

Epics 1–3 build the engine and the interface. This epic makes it **production-worthy**. It covers everything an operator, security reviewer, or compliance auditor would demand before the platform handles real internet-scale traffic: identity and access control, per-consumer rate limiting, structured telemetry pipelines, operational dashboards, alerting, dead-letter management, suppression workflows, and audit trails.

---

## 2. Deliverables

| # | Deliverable | Description |
|---|-------------|-------------|
| D4.1 | **API authentication & authorisation** | Token-based authentication (API keys or OAuth2 client credentials), RBAC for operators and API consumers, scoped read-only access. |
| D4.2 | **Rate limiting & quota enforcement** | Per-consumer rate limits on all API endpoints, query quotas, abuse detection, `Retry-After` headers. |
| D4.3 | **Internal service authentication** | Worker-to-queue and worker-to-DB authentication via managed identity or rotated secrets. Encrypted traffic in transit. |
| D4.4 | **Encryption at rest** | Database-level encryption (Postgres TDE or volume-level encryption). Redis encryption if applicable. |
| D4.5 | **Structured telemetry pipeline** | OpenTelemetry-based collection of logs, metrics, and traces from all services, workers, and queues, exported to a centralised backend. |
| D4.6 | **Operational dashboards** | Pre-built dashboards covering: scan coverage, queue health, worker throughput, failure rates, API latency, cache effectiveness, rule-level metrics. |
| D4.7 | **Alerting rules** | Automated alerts for: queue backlog thresholds, worker failure spikes, dead-letter queue growth, API latency degradation, scan coverage drop, rule execution errors. |
| D4.8 | **Dead-letter queue management** | Admin tooling to inspect, retry, and purge dead-letter queues. Dashboard visibility into DLQ depth and contents. |
| D4.9 | **Audit trail system** | Complete audit log for: config/rule changes, API key management, suppression list changes, admin actions, operator interventions. |
| D4.10 | **Suppression & compliance workflow** | Suppression list management (exclude targets/ports/CIDRs from scanning). Legal/operational approval workflow for higher-cost checks. Scope control. |
| D4.11 | **Deployment & infrastructure-as-code** | Dockerfiles for all services, Kubernetes manifests or Helm charts (if applicable), CI/CD pipeline definitions, environment configuration management. |

---

## 3. High-Level Tasks

### 3.1 API Authentication & Authorisation

| # | Task | Details |
|---|------|---------|
| T4.1.1 | Implement **API key authentication** | Generate, store (hashed), and validate API keys. Keys are scoped to a consumer identity. Support key rotation without downtime. |
| T4.1.2 | Implement **OAuth2 client credentials** (optional) | Support OAuth2 client-credentials flow for machine-to-machine consumers. Validate JWT tokens issued by a trusted identity provider. |
| T4.1.3 | Implement **RBAC model** | Define roles: `reader` (standard API access), `analyst` (extended search / export), `operator` (admin endpoints, DLQ management), `admin` (config changes, key management). |
| T4.1.4 | Implement **authorisation middleware** | ASP.NET Core policy-based authorisation. Enforce role requirements per endpoint. Reject unauthorised requests with 403. |
| T4.1.5 | Implement **API key management endpoints** | Admin-only endpoints to create, list, revoke, and rotate API keys. Log all key management actions to audit trail. |
| T4.1.6 | Implement **tenant isolation** (future-ready) | Design the auth model so that per-tenant scoping can be added later without a rewrite. Include a `tenant_id` claim/field even if single-tenant initially. |

### 3.2 Rate Limiting & Quota Enforcement

| # | Task | Details |
|---|------|---------|
| T4.2.1 | Implement **per-consumer rate limiter** | Use Redis counters (sliding window or token bucket) keyed by consumer identity. Configurable limits per role/consumer. |
| T4.2.2 | Implement **global rate limiter** | Platform-wide request ceiling to protect backend resources under extreme load. |
| T4.2.3 | Return **standard rate-limit headers** | `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset`, `Retry-After` on 429 responses. |
| T4.2.4 | Implement **query cost estimation** | Assign a cost weight to different query types (single IP = 1, filtered search = 5, history export = 50). Deduct from per-consumer quota. |
| T4.2.5 | Implement **abuse detection** | Detect patterns: excessive 429s, rapid key cycling, abnormal query patterns. Flag for operator review. Log to audit trail. |

### 3.3 Internal Service Authentication

| # | Task | Details |
|---|------|---------|
| T4.3.1 | Configure **worker-to-Redis authentication** | Use Redis AUTH with strong passwords or ACLs. Rotate credentials via configuration. |
| T4.3.2 | Configure **worker-to-Postgres authentication** | Use managed identity (if cloud-hosted) or rotated connection-string secrets. No hardcoded credentials in code or config files. |
| T4.3.3 | Enforce **TLS for all internal traffic** | Redis connections over TLS. Postgres connections over TLS with certificate verification. Inter-service calls over TLS. |
| T4.3.4 | Implement **secret management** | Use a secret store (Azure Key Vault, HashiCorp Vault, or environment-injected secrets). Rotate secrets on a schedule. |

### 3.4 Encryption at Rest

| # | Task | Details |
|---|------|---------|
| T4.4.1 | Enable **Postgres encryption at rest** | Use Transparent Data Encryption or volume-level encryption depending on hosting. |
| T4.4.2 | Enable **Redis encryption at rest** | If using managed Redis (e.g., Azure Cache for Redis), enable encryption at rest. If self-hosted, use encrypted volumes. |
| T4.4.3 | Document **encryption posture** | Record what is encrypted, with what mechanism, and key rotation policy. |

### 3.5 Structured Telemetry Pipeline

| # | Task | Details |
|---|------|---------|
| T4.5.1 | Finalise **OpenTelemetry configuration** | All services and workers export traces, metrics, and logs via OTLP to a centralised collector (e.g., Grafana Alloy, OpenTelemetry Collector). |
| T4.5.2 | Define **standard metric names** | Consistent naming across all services. Examples: `scan.probes.total`, `scan.probes.success`, `queue.depth`, `queue.dlq.depth`, `api.request.duration`, `cache.hit_ratio`, `enrichment.rule.executions`. |
| T4.5.3 | Define **standard log structure** | All logs include: timestamp, service name, trace ID, span ID, severity, message, structured properties. Use Serilog or Microsoft.Extensions.Logging with OTLP exporter. |
| T4.5.4 | Implement **distributed trace correlation** | Propagate trace context from scheduler → queue → worker → database. API requests carry trace context through cache and DB calls. |
| T4.5.5 | Configure **log retention and rotation** | Define retention policies: hot logs (7 days), warm logs (30 days), cold archive (1 year). Configure accordingly in the logging backend. |

### 3.6 Operational Dashboards

| # | Task | Details |
|---|------|---------|
| T4.6.1 | **Scan Coverage dashboard** | Targets due vs scanned, scan completion percentage, targets overdue, coverage by shard. |
| T4.6.2 | **Queue Health dashboard** | Queue depth by queue name/shard, enqueue rate, dequeue rate, consumer lag, dead-letter queue depth. |
| T4.6.3 | **Worker Throughput dashboard** | Active workers by type, jobs processed per minute, success/failure/retry counts, average job duration, worker CPU/memory. |
| T4.6.4 | **Stage Conversion dashboard** | Stage 1 → classification rate, classification → enrichment flagging rate, enrichment success/failure rate. |
| T4.6.5 | **API Performance dashboard** | Request rate, p50/p95/p99 latency by endpoint, error rate by status code, cache hit ratio, rate-limit trigger count. |
| T4.6.6 | **Exposure Insights dashboard** | Most common open ports, service type distribution, severity distribution, new exposures trend, top changed IPs. |
| T4.6.7 | **Rule Performance dashboard** | Per-rule execution count, success/failure rate, average duration, cost consumed, error types. |
| T4.6.8 | **Cache Effectiveness dashboard** | Redis memory usage, key count by prefix, cache hit/miss ratio, eviction rate, TTL distribution. |

### 3.7 Alerting Rules

| # | Task | Details |
|---|------|---------|
| T4.7.1 | **Queue backlog alert** | Trigger when any queue depth exceeds N for > M minutes. Configurable per queue. |
| T4.7.2 | **Dead-letter queue growth alert** | Trigger when DLQ depth increases by > N in a time window. |
| T4.7.3 | **Worker failure spike alert** | Trigger when worker error rate exceeds threshold (e.g., > 5% of jobs failing). |
| T4.7.4 | **API latency degradation alert** | Trigger when p95 latency exceeds SLO (50 ms for cached lookups, 300 ms for searches, 2 s for history). |
| T4.7.5 | **Scan coverage drop alert** | Trigger when daily scan completion falls below expected rate for > 1 day. |
| T4.7.6 | **Rule execution error alert** | Trigger when a specific enrichment rule exceeds error threshold. Auto-disable rule if critical. |
| T4.7.7 | **Redis memory pressure alert** | Trigger when Redis memory usage exceeds configured threshold (e.g., 80%). |
| T4.7.8 | **Database connection pool exhaustion alert** | Trigger when available connections drop below threshold. |

### 3.8 Dead-Letter Queue Management

| # | Task | Details |
|---|------|---------|
| T4.8.1 | Implement **DLQ inspection API/CLI** | List dead-lettered jobs with: original queue, payload summary, error message, attempt count, timestamps. Supports filtering and pagination. |
| T4.8.2 | Implement **DLQ retry** | Operator can re-enqueue selected dead-lettered jobs back to their source queue for reprocessing. |
| T4.8.3 | Implement **DLQ purge** | Operator can delete dead-lettered jobs (individually or in bulk) after review. Logged to audit trail. |
| T4.8.4 | Implement **poison rule isolation** | If a specific rule config causes repeated DLQ entries, auto-flag the rule. Operators can disable it from the DLQ management interface. |
| T4.8.5 | Add **DLQ metrics to dashboards** | DLQ depth by source queue, age of oldest DLQ item, DLQ growth rate. |

### 3.9 Audit Trail System

| # | Task | Details |
|---|------|---------|
| T4.9.1 | Define **auditable events** | Rule config create/update/activate/deactivate/rollback, API key create/revoke/rotate, suppression rule create/update/delete, DLQ retry/purge, operator admin actions, target inventory import. |
| T4.9.2 | Implement **audit event recording** | Write structured audit events to the `AuditEvent` table: timestamp, actor, action, entity_type, entity_id, old_value, new_value, metadata. |
| T4.9.3 | Implement **audit query endpoint** | Operator/admin-only endpoint to search audit events by actor, action, entity, time range. |
| T4.9.4 | Implement **audit log immutability** | Audit table is append-only. No UPDATE or DELETE allowed at the application level. Enforce with DB permissions or triggers. |

### 3.10 Suppression & Compliance Workflow

| # | Task | Details |
|---|------|---------|
| T4.10.1 | Implement **suppression list management** | CRUD for suppression rules: suppress by IP, CIDR, port, service type, or combination. Each rule has a reason, creator, expiry date. |
| T4.10.2 | Implement **suppression enforcement** | Scheduler skips suppressed targets. Workers skip suppressed port/service combinations. State machine transitions to `Suppressed`. |
| T4.10.3 | Implement **scope control** | Define the allowed scanning scope (IP ranges, port ranges). Reject out-of-scope targets at import and scheduling time. |
| T4.10.4 | Implement **approval workflow for high-cost checks** | Certain enrichment rules (above a cost threshold) require operator approval before activation. Approval recorded in audit trail. |
| T4.10.5 | Implement **compliance reporting** | Generate reports: active suppressions, scope boundaries, rule change history, approval records. Exportable for auditors. |

### 3.11 Deployment & Infrastructure

| # | Task | Details |
|---|------|---------|
| T4.11.1 | Author **Dockerfiles** | Multi-stage Dockerfiles for: API service, scheduler service, each worker type. Optimise for image size and build caching. |
| T4.11.2 | Author **Docker Compose (production-like)** | Compose file that runs the full platform: Postgres, Redis, API, scheduler, workers, telemetry collector. |
| T4.11.3 | Author **Kubernetes manifests / Helm charts** (if applicable) | Deployments, services, config maps, secrets, horizontal pod autoscalers for workers, resource limits. |
| T4.11.4 | Implement **CI/CD pipeline** | Build, test, publish Docker images, run migrations, deploy. Separate stages for dev/staging/production. |
| T4.11.5 | Implement **environment configuration management** | Per-environment settings (connection strings, Redis endpoints, feature flags, scan policies) managed via environment variables, config maps, or a secret store. No secrets in source control. |
| T4.11.6 | Implement **graceful shutdown for all services** | All workers and services handle SIGTERM: stop accepting new work, finish in-progress work (within timeout), release leases, flush telemetry, exit cleanly. |

---

## 4. Acceptance Criteria

1. API endpoints reject unauthenticated requests with 401 and unauthorised requests with 403.
2. Rate limiting correctly enforces per-consumer limits and returns standard rate-limit headers.
3. Abuse detection flags consumers exceeding abnormal request patterns.
4. All internal service connections (Redis, Postgres, inter-service) use TLS.
5. No credentials are hardcoded in source code or committed to version control.
6. OpenTelemetry traces propagate end-to-end from API request through cache/DB, and from scheduler through queue through worker.
7. All eight operational dashboards render with live data and provide actionable insights.
8. Alerting rules fire correctly when simulated threshold breaches occur.
9. Dead-letter queue management allows inspection, retry, and purge with full audit logging.
10. Audit trail captures all defined auditable events and is queryable by operators.
11. Suppression rules correctly prevent scanning of suppressed targets/ports.
12. All services shut down gracefully within the configured timeout on SIGTERM.
13. CI/CD pipeline builds, tests, and deploys the full platform to at least one environment.

---

## 5. Dependencies

| Dependency | Source |
|------------|--------|
| Shared telemetry bootstrap and health-check helpers | Epic 1 |
| Queue transport library (for DLQ management) | Epic 1 |
| Database schema (AuditEvent, SuppressionRule tables) | Epic 1 |
| Worker services to instrument (scheduler, scan workers, enrichment workers) | Epic 2 |
| API service to secure and instrument | Epic 3 |

---

## 6. Risks

| Risk | Mitigation |
|------|------------|
| Security review may require changes across all epics | Start security design early; involve security review during Epic 1 design. |
| Dashboard tooling choice affects development effort | Pick a single observability stack early (e.g., Grafana + Prometheus + Loki + Tempo) and standardise. |
| Rate limiting at scale may add latency to every request | Use Redis-based rate limiting with sub-millisecond overhead; benchmark under load. |
| Audit log table may grow very large | Partition by time; define retention and archival policies. |
| Compliance requirements may vary by jurisdiction | Design suppression and scope control to be flexible; document extensibility points. |

---

## 7. Estimated Scope

| Category | Rough Effort |
|----------|-------------|
| API authentication & authorisation | Medium–Large |
| Rate limiting & quotas | Medium |
| Internal service authentication & encryption | Medium |
| Telemetry pipeline | Medium |
| Operational dashboards (8) | Large |
| Alerting rules | Medium |
| Dead-letter queue management | Medium |
| Audit trail system | Medium |
| Suppression & compliance workflow | Medium |
| Deployment & infrastructure | Medium–Large |

---

*This epic runs partially in parallel with Epics 2 and 3 — telemetry and auth foundations should start early, while dashboards and alerting mature as the system produces real data.*
