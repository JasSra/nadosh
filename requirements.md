Below is a **requirements draft** for the system you described.

# 1. Product summary

Build a **large-scale internet exposure intelligence platform** that continuously scans public IPv4 space for selected signals such as:

1. Open ports
2. TLS certificates
3. Basic service fingerprints
4. High-risk exposed services
5. Config-driven protocol/service checks in second-stage analysis

The system must:

1. Use **workers + queues** for all heavy tasks
2. Use **Redis** for fast state, queueing, caching, rate control, hot reads
3. Use **PostgreSQL or SQL Server** for durable, queryable historical storage
4. Expose a **read-only API** for very fast querying by IP, CIDR, domain, port, service, tags, and time range
5. Support **multi-stage scanning and enrichment**
6. Be **config-driven** so new checks can be added without redeploying core logic

---

# 2. Core goals

## 2.1 Functional goals

The platform shall:

1. Discover and track internet-exposed services across a large target set
2. Re-scan targets on a rolling cadence, such as every **15 days**
3. Detect changes in exposure over time
4. Flag targets for deeper protocol-specific inspection
5. Store both:

   1. latest snapshot
   2. historical observations
6. Provide near real-time query access to current state
7. Allow declarative authoring of service-specific checks

## 2.2 Non-functional goals

The platform shall be:

1. Horizontally scalable
2. Queue-driven and fault-tolerant
3. Idempotent under retries
4. Cost-aware
5. Safe to operate at large scale
6. Observable end to end
7. Able to handle **tens of millions of IPs** and **hundreds of millions to billions of observations**

---

# 3. High-level architecture

## 3.1 Main components

1. **Scheduler service**

   * decides what to scan
   * slices work into batches
   * enqueues jobs

2. **Stage 1 scan workers**

   * perform broad, cheap scans
   * common ports
   * TLS/cert collection
   * basic banners/fingerprints

3. **Stage 2 enrichment workers**

   * pick flagged results
   * apply protocol/service-specific checks
   * driven by declarative configs

4. **API service**

   * read-only query endpoints
   * hot path from Redis
   * historical path from Postgres/SQL

5. **Redis**

   * queue transport or queue coordination
   * hot cache
   * dedupe keys
   * rate limits
   * job leases
   * recent snapshots / materialized query results

6. **Persistent DB**

   * durable historical data
   * indexed latest state
   * analytics and range queries
   * audit trail

7. **Config registry**

   * stores service check definitions
   * versioned
   * validated before activation

8. **Telemetry/ops stack**

   * logs
   * metrics
   * traces
   * job dashboards
   * failure queues

---

# 4. Scan model

## 4.1 Stage 1: broad discovery

Purpose: low-cost internet-wide or target-set-wide discovery.

Inputs:

1. IP ranges / target inventory
2. allowed common ports list
3. scan cadence policy
4. exclusions / allowlists / suppressions

Outputs:

1. observed open ports
2. protocol guess
3. TLS metadata if applicable
4. service banner where safe and cheap
5. response timing
6. fingerprint confidence
7. candidate flags for Stage 2

Typical collected attributes:

1. IP
2. port
3. transport protocol
4. timestamp
5. TLS cert subject/SAN/issuer/expiry/fingerprint
6. HTTP title/server header if applicable
7. SSH banner
8. RDP presence
9. SNMP presence indicator only if permitted by policy
10. preliminary service classification

## 4.2 Stage 2: enrichment

Purpose: more targeted and more expensive checks.

Triggered when:

1. Stage 1 identifies a known port/service signature
2. a certificate/domain matches interest patterns
3. a host changed state
4. a rule explicitly requests deeper validation

Characteristics:

1. queue-driven
2. config-driven
3. retryable
4. timeout-bounded
5. isolated per protocol/check type

Outputs:

1. enriched fingerprint
2. exposure severity
3. product/vendor/version clues
4. issue tags
5. evidence summary
6. next recommended rescan interval

---

# 5. Scale assumptions

Design for:

1. Up to **56 million IPv4 targets**
2. Rolling scan window of **15 days**
3. Multiple ports per target
4. Burst scheduling
5. Large variance in latency and host responsiveness

Derived planning view:

1. 56,000,000 IPs / 15 days = about **3.73 million IPs/day**
2. At 10 common ports, that is about **37.3 million probe attempts/day**
3. That is about **432 probe attempts/second** average
4. Real throughput must be much higher because:

   1. retries
   2. timeouts
   3. uneven scheduling
   4. enrichment load
   5. maintenance windows
   6. regional sharding

So practical design should target at least:

1. **5,000–20,000 lightweight probe ops/sec** aggregate capability
2. separate enrichment capacity pool
3. independent per-region throttles

That is where queueing and sharding stop this from becoming a bonfire with dashboards.

---

# 6. Data storage requirements

## 6.1 Redis responsibilities

Redis shall be used for:

1. active job queues
2. delayed jobs
3. worker leases / heartbeats
4. deduplication keys
5. rate limit counters
6. recent target state cache
7. hot query cache
8. distributed locks where necessary
9. rolling aggregation caches

Redis shall **not** be the source of truth for long-term history.

## 6.2 Persistent DB responsibilities

Postgres or SQL Server shall store:

1. target inventory
2. scan schedules
3. observations history
4. latest materialized state
5. enrichment results
6. config versions
7. audit events
8. API query metadata
9. suppression rules
10. tenancy and access metadata if multi-tenant later

## 6.3 Storage pattern

Use a **dual model**:

1. **Append-only observations table** for history
2. **Current state table** for latest known exposure per asset/service

That gives:

1. fast latest lookup
2. history over time
3. change tracking
4. easy cache refresh

---

# 7. Suggested logical data model

## 7.1 Core entities

1. **Target**

   * ip
   * cidr/source
   * ownership tags
   * monitored flag
   * last scheduled
   * next scheduled

2. **Observation**

   * target_id
   * observed_at
   * port
   * protocol
   * state
   * latency_ms
   * fingerprint
   * evidence blob
   * scan_run_id

3. **CurrentExposure**

   * target_id
   * port
   * protocol
   * current_state
   * first_seen
   * last_seen
   * last_changed
   * classification
   * severity
   * cached_summary

4. **CertificateObservation**

   * target_id
   * port
   * sha256
   * subject
   * san list
   * issuer
   * valid_from
   * valid_to
   * observed_at

5. **EnrichmentResult**

   * observation_id or current_exposure_id
   * rule_id
   * rule_version
   * result_status
   * confidence
   * tags
   * summary
   * evidence blob
   * executed_at

6. **RuleConfig**

   * rule_id
   * version
   * service type
   * trigger conditions
   * request definition
   * matcher definition
   * severity mapping
   * enabled flag

7. **ScanRun**

   * run_id
   * stage
   * shard
   * started_at
   * completed_at
   * worker id
   * counts
   * status

---

# 8. Queue and worker requirements

## 8.1 Queue requirements

The queueing layer shall support:

1. delayed jobs
2. priority queues
3. retries with backoff
4. poison/dead-letter queues
5. job leasing / visibility timeout
6. idempotency keys
7. shard-aware dispatch
8. scheduling by next-run timestamp

## 8.2 Worker requirements

Workers shall:

1. be stateless
2. scale horizontally
3. renew job leases while active
4. emit structured telemetry
5. support graceful shutdown
6. checkpoint long jobs where possible
7. enforce strict timeout budgets
8. handle duplicate delivery safely

## 8.3 Worker classes

1. **Planner worker**

   * calculates next due targets

2. **Stage 1 scan worker**

   * executes lightweight probes

3. **Classifier worker**

   * normalizes results into common service model

4. **Stage 2 enrichment worker**

   * runs declarative service-specific checks

5. **Cache projector worker**

   * updates Redis hot views

6. **Compaction/materialization worker**

   * rolls history into current tables and aggregates

---

# 9. Declarative rule/config system

This is one of the most important parts.

## 9.1 Requirements

The system shall allow service checks to be defined through config, not hardcoded logic, where possible.

Each config should define:

1. identity
2. version
3. trigger conditions
4. input requirements
5. protocol type
6. request pattern
7. response matcher
8. evidence extraction
9. severity mapping
10. timeout / retry policy
11. cache TTL
12. output schema

## 9.2 Example rule categories

1. TLS certificate collector
2. HTTP header/title collector
3. SSH banner parser
4. RDP presence checker
5. RTSP/camera signature matcher
6. SNMP metadata collector
7. SMTP banner collector
8. web admin panel classifier

## 9.3 Rule engine constraints

1. configs must be validated before activation
2. versioned rollouts required
3. rules must be sandboxed by capability
4. per-rule concurrency limits required
5. per-rule cost score required
6. all outputs must map to a canonical schema

---

# 10. API requirements

## 10.1 API goals

The API shall provide a **read-only**, high-speed interface for:

1. current exposure lookup
2. historical exposure queries
3. certificate lookup
4. domain-to-IP and cert-to-IP correlations
5. trending and changes over time
6. service-specific filtering

## 10.2 Query dimensions

Support filters by:

1. IP
2. CIDR
3. domain
4. certificate fingerprint
5. port
6. protocol
7. product/vendor
8. tags
9. severity
10. time range
11. first seen / last seen
12. change status

## 10.3 Query behaviour

Hot path:

1. Redis first for:

   1. recent/current lookups
   2. common search results
   3. precomputed aggregations

Cold/history path:

1. Postgres/SQL for:

   1. time-series/history
   2. large result sets
   3. exports
   4. long-range analytics

## 10.4 API style

Recommended:

1. REST for common access
2. optional query DSL endpoint for advanced filtering
3. cursor pagination, not offset, at scale

Examples:

1. `/v1/exposures/{ip}`
2. `/v1/exposures/search`
3. `/v1/certificates/{fingerprint}`
4. `/v1/history/search`
5. `/v1/stats/ports`
6. `/v1/stats/trends`

## 10.5 Query DSL requirement

Provide a constrained query language similar in spirit to KQL.

Must support:

1. boolean operators
2. equality
3. contains
4. range filters
5. in-list filters
6. sorting
7. paging
8. field allowlist only

Example:

```text
ip = "1.2.3.4" AND port IN (80,443) AND lastSeen >= "2026-03-01T00:00:00Z"
```

Do **not** allow arbitrary SQL execution.

---

# 11. Scheduling requirements

## 11.1 Cadence

The scheduler shall support:

1. default full-cycle target revisit every **15 days**
2. dynamic shorter intervals for:

   1. recently changed hosts
   2. high-risk services
   3. new discoveries
3. longer intervals for stable low-interest targets

## 11.2 Scheduling strategy

Scheduling shall be:

1. shard-aware
2. priority-based
3. cost-aware
4. resumable
5. deterministic enough for auditability

Recommended prioritisation inputs:

1. monitored/interesting targets first
2. changed-state targets next
3. high-risk service signatures next
4. background internet-wide sweep last

---

# 12. Performance requirements

## 12.1 Read API

1. single IP current-state query: **p95 under 50 ms** from cache
2. small filtered search: **p95 under 300 ms**
3. historical queries over indexed ranges: **p95 under 2 s**
4. large exports: async job or streamed cursor response

## 12.2 Workers

1. worker retries must not corrupt state
2. all stage transitions must be idempotent
3. scan results ingestion must support batch upserts
4. DB writes should use bulk operations where possible

## 12.3 Cache

1. latest-exposure cache must be refreshed within seconds of write
2. cached search TTLs should be configurable
3. hot item invalidation must be event-driven, not only TTL-driven

---

# 13. DB design requirements

## 13.1 PostgreSQL recommendation

If using Postgres:

1. partition observation tables by time, e.g. monthly
2. index current-state by ip, port, protocol
3. use JSONB for flexible evidence blobs
4. use materialized rollups where helpful
5. consider TimescaleDB only if time-series pressure becomes dominant

## 13.2 SQL Server alternative

If using SQL Server:

1. use partitioned tables for history
2. use clustered indexing carefully on hot current-state tables
3. use JSON columns for evidence payloads
4. use columnstore for large analytical history tables if needed

## 13.3 Current vs history split

This is important:

1. **CurrentExposure** should be small and heavily indexed
2. **ObservationHistory** should be append-heavy and partitioned
3. analytical queries should prefer rollup tables where possible

---

# 14. Redis design requirements

Use Redis for:

1. queue lists/streams
2. sorted sets for next-run scheduling
3. hashes for latest state cache
4. sets for dedupe
5. counters for rate limits
6. ephemeral search caches

Recommended keys:

1. `scan:schedule:{shard}`
2. `job:lease:{jobId}`
3. `target:last:{ip}`
4. `exposure:current:{ip}`
5. `querycache:{hash}`
6. `dedupe:stage1:{target}:{port}:{window}`
7. `dedupe:stage2:{obs}:{ruleVersion}`

Do not store massive permanent search index data in Redis unless it is truly hot.

---

# 15. Security and access requirements

## 15.1 Platform security

1. all internal service traffic authenticated
2. worker-to-queue and worker-to-DB auth via managed identity or rotated secrets
3. encrypted data in transit
4. encrypted data at rest
5. audit logs for admin/config changes
6. RBAC for operators and API consumers

## 15.2 API security

1. read-only tokens
2. scoped API keys or OAuth2 client credentials
3. rate limiting per consumer
4. query quotas
5. abuse detection
6. tenant isolation if multi-tenant later

## 15.3 Compliance guardrails

1. explicit scope control
2. suppression lists
3. legal/operational approval workflow for higher-cost checks
4. full audit trail for rule changes and execution

---

# 16. Observability requirements

The system shall emit:

1. structured logs
2. queue depth metrics
3. worker throughput
4. success/failure/retry counts
5. stage latency
6. DB latency
7. cache hit ratio
8. scan coverage ratio
9. API p50/p95/p99 latency
10. rule-level cost and error metrics

Dashboards should show:

1. targets due vs scanned
2. stage 1 to stage 2 conversion rate
3. most common exposed ports
4. service classification trends
5. failures by worker type
6. queue backlog by shard
7. cache effectiveness

---

# 17. Failure handling requirements

1. job retry with exponential backoff
2. dead-letter queue for repeated failures
3. poison rule/config isolation
4. partial batch failure handling
5. idempotent reprocessing
6. worker crash recovery via lease timeout
7. scheduler recovery after restart without duplicate floods

---

# 18. Multi-stage state machine

A target or finding should move through states like:

1. `Scheduled`
2. `Stage1Scanning`
3. `Stage1Observed`
4. `Classified`
5. `FlaggedForEnrichment`
6. `Stage2Queued`
7. `Stage2Processing`
8. `Enriched`
9. `Suppressed`
10. `Error`
11. `Completed`

This should be persisted, not just implied.

---

# 19. MVP scope

## 19.1 MVP features

1. target inventory import
2. rolling scheduler
3. stage 1 common-port scanner
4. TLS certificate collection
5. simple service classification
6. stage 2 config framework
7. 2 to 4 initial declarative checks
8. current-state cache in Redis
9. historical storage in Postgres
10. read-only search API
11. dashboards and dead-letter handling

## 19.2 Post-MVP

1. domain enrichment
2. ASN/geolocation tagging
3. risk scoring
4. saved queries
5. change notifications
6. tenant support
7. export/reporting jobs
8. advanced DSL
9. rule marketplace / plugin packs
10. IPv6 strategy

---

# 20. Suggested tech shape

## 20.1 Backend

Given your background, a strong fit is:

1. **.NET**
2. worker services using hosted services
3. Redis
4. PostgreSQL
5. API in ASP.NET Core
6. OpenTelemetry
7. Docker/Kubernetes later if needed

## 20.2 Why Postgres over SQL Server here

For this workload, I would lean **Postgres** first because:

1. excellent partitioning support
2. JSONB flexibility
3. strong ecosystem for time-series-ish workloads
4. generally lower cost footprint
5. easier horizontal operational patterns in many cases

SQL Server is still fine if:

1. your team already operates it well
2. you want tight Microsoft ecosystem alignment
3. reporting stack already depends on it

---

# 21. Example requirement statements

You can lift these into a formal document.

## Functional

1. The system shall schedule target scans on a rolling cadence with a default revisit interval of 15 days.
2. The system shall support broad stage-1 probing across a target inventory of at least 56 million IPv4 addresses.
3. The system shall enqueue stage-2 enrichment jobs based on configurable triggers derived from stage-1 results.
4. The system shall support declarative service-check configurations with versioning and validation.
5. The system shall persist all scan observations in a durable historical store.
6. The system shall maintain a latest-known exposure view per target and service.
7. The system shall expose read-only APIs for current and historical search.
8. The system shall support filtering by IP, CIDR, domain, port, service, severity, tags, and time range.
9. The system shall query Redis first for hot results and fall back to the persistent store for historical or uncached queries.
10. The system shall record all worker state transitions and failures for audit and recovery.

## Non-functional

1. The system shall scale horizontally by adding worker instances without requiring application redesign.
2. The system shall tolerate duplicate job delivery and retries without producing incorrect persisted state.
3. The system shall maintain p95 latency below 50 ms for cached single-IP exposure lookups.
4. The system shall maintain p95 latency below 300 ms for common filtered read queries served from cache or indexed current-state tables.
5. The system shall support structured telemetry for all services, workers, and queues.
6. The system shall provide dead-letter handling for failed jobs.
7. The system shall enforce authentication, authorization, and rate limiting on all API endpoints.
8. The system shall version and audit all active enrichment rules.

---

# 22. Architecture recommendation in one line

Use **Redis for orchestration + hot path**, **Postgres for truth + history**, **.NET workers for staged scanning/enrichment**, and a **read-only query API** over a **latest-state + append-only history** model.

---

# 23. My direct recommendation

If you want this to stay sane at scale, start with these hard rules:

1. **Separate current-state from history**
2. **Make every stage idempotent**
3. **Keep stage 1 cheap**
4. **Make stage 2 strictly config/version driven**
5. **Use Redis only for hot and ephemeral data**
6. **Partition the history tables early**
7. **Use cursor-based API pagination**
8. **Treat scheduling as a first-class subsystem**

If you want, next I can turn this into a proper **engineering requirements document** with sections like:

1. Scope
2. Assumptions
3. Functional requirements
4. Non-functional requirements
5. Architecture
6. Data model
7. API contract
8. Delivery phases

Or I can draft the **actual .NET service breakdown and schema**.
