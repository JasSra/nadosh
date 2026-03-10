# Epic 3 — Read-Only API & Query Layer

> **Goal:** Deliver a high-performance, read-only REST API with a constrained query DSL that serves current-exposure lookups, historical searches, certificate queries, and statistical aggregations — using Redis for the hot path and PostgreSQL for the cold/historical path.

---

## 1. Epic Overview

This epic is the **public interface** of the platform. Every consumer — analysts, dashboards, integrations, downstream systems — interacts exclusively through this API. It must be fast (p95 < 50 ms for cached single-IP lookups), secure (authenticated, rate-limited, scoped), and flexible enough to support both simple lookups and complex filtered searches without exposing raw SQL.

---

## 2. Deliverables

| # | Deliverable | Description |
|---|-------------|-------------|
| D3.1 | **ASP.NET Core API service** | A production-grade read-only REST API with structured routing, model validation, error handling, and OpenTelemetry integration. |
| D3.2 | **Current-exposure endpoints** | Fast single-IP and batch lookup of current exposure state, served primarily from Redis cache. |
| D3.3 | **Search endpoints** | Filtered search across current exposures by IP, CIDR, domain, port, protocol, service, severity, tags, and time range. |
| D3.4 | **Historical query endpoints** | Time-series / history search over the observation archive in PostgreSQL, with cursor pagination and async export support. |
| D3.5 | **Certificate lookup endpoints** | Lookup by certificate SHA-256 fingerprint. Reverse lookup: certificate → IPs. Domain → certificate → IPs correlation. |
| D3.6 | **Statistics & trends endpoints** | Pre-computed and on-demand aggregations: top ports, service distributions, severity trends, change rates. |
| D3.7 | **Query DSL engine** | A constrained, KQL-inspired query language supporting boolean operators, equality, contains, range, in-list, sorting, paging, and a strict field allowlist. |
| D3.8 | **Caching strategy implementation** | Dual-path query execution: Redis-first for hot lookups, Postgres fallback for historical/uncached. Event-driven cache invalidation. Configurable TTLs. |
| D3.9 | **API documentation** | OpenAPI / Swagger specification. Query DSL reference. Example requests and responses. Rate limit documentation. |

---

## 3. High-Level Tasks

### 3.1 API Service Foundation

| # | Task | Details |
|---|------|---------|
| T3.1.1 | Scaffold **ASP.NET Core Web API** project | Minimal API or controller-based. Configure JSON serialization (System.Text.Json, camelCase). Wire up dependency injection for repositories, Redis, telemetry. |
| T3.1.2 | Implement **global error handling** | Consistent problem-details (RFC 7807) error responses. Map domain exceptions to HTTP status codes. Never leak internal details. |
| T3.1.3 | Implement **request validation** | FluentValidation or Data Annotations for all request models. Reject invalid inputs early with clear messages. |
| T3.1.4 | Implement **cursor-based pagination** | All list/search endpoints use opaque cursor tokens, **not** offset-based pagination. Support configurable page sizes with a hard upper limit. |
| T3.1.5 | Implement **OpenTelemetry integration** | Traces per request. Metrics: request count, latency (p50/p95/p99), error rate, cache-hit ratio. Use the shared telemetry bootstrap from Epic 1. |
| T3.1.6 | Implement **health and readiness endpoints** | `/health/live` and `/health/ready` checking Postgres and Redis connectivity. |

### 3.2 Current-Exposure Endpoints

| # | Task | Details |
|---|------|---------|
| T3.2.1 | `GET /v1/exposures/{ip}` | Return current exposure summary for a single IP. **Hot path**: read from `exposure:current:{ip}` Redis hash first. Fallback to `CurrentExposure` table. Target p95 < 50 ms. |
| T3.2.2 | `POST /v1/exposures/batch` | Accept a list of IPs (max 100). Return current exposures for all. Pipeline Redis reads. |
| T3.2.3 | `GET /v1/exposures/{ip}/ports` | Return per-port breakdown for an IP: port, protocol, state, service, severity, first_seen, last_seen, last_changed. |
| T3.2.4 | `GET /v1/exposures/{ip}/enrichments` | Return enrichment results associated with the IP's current exposures. |
| T3.2.5 | Implement **Redis-first read path** | Check Redis hash. On miss: query Postgres, return result, asynchronously populate cache. Track cache hit/miss metrics. |

### 3.3 Search Endpoints

| # | Task | Details |
|---|------|---------|
| T3.3.1 | `POST /v1/exposures/search` | Accept a structured filter object or a query DSL string. Return matching current exposures with cursor pagination. Target p95 < 300 ms for common filtered searches. |
| T3.3.2 | Implement **filter dimensions** | Support filtering by: IP, CIDR range, domain, certificate fingerprint, port, protocol, product/vendor, tags, severity, time range (first_seen/last_seen), change status (new/changed/stable/gone). |
| T3.3.3 | Implement **search result caching** | Hash the query parameters → `querycache:{hash}`. Cache results in Redis with configurable TTL. Invalidate on relevant data changes (event-driven from Epic 2 cache projector). |
| T3.3.4 | Implement **CIDR range expansion** | Translate CIDR filter into an efficient IP-range predicate on the database. Use PostgreSQL `inet`/`cidr` types or a numeric-range index strategy. |
| T3.3.5 | Implement **sort options** | Sort by: last_seen (default), first_seen, severity, port, IP. Ascending/descending. |

### 3.4 Historical Query Endpoints

| # | Task | Details |
|---|------|---------|
| T3.4.1 | `POST /v1/history/search` | Search the `Observation` history table by IP, port, time range, scan_run_id. Return with cursor pagination. Target p95 < 2 s for indexed range queries. |
| T3.4.2 | `GET /v1/history/{ip}` | Return observation timeline for a single IP, optionally filtered by port and time range. |
| T3.4.3 | `GET /v1/history/{ip}/changes` | Return only observations where the state changed compared to the prior observation for that IP+port. |
| T3.4.4 | Implement **async export** for large result sets | If the result set exceeds a threshold, return a job ID. The export runs asynchronously and produces a downloadable file (CSV/JSON). |
| T3.4.5 | Ensure **partition pruning** | All historical queries must include a time range that allows PostgreSQL to prune partitions. Enforce a mandatory time range or default window. |

### 3.5 Certificate Lookup Endpoints

| # | Task | Details |
|---|------|---------|
| T3.5.1 | `GET /v1/certificates/{fingerprint}` | Return certificate details by SHA-256 fingerprint: subject, SAN list, issuer, validity, all IPs/ports where it was observed. |
| T3.5.2 | `POST /v1/certificates/search` | Search certificates by subject, SAN, issuer, expiry range, or associated IP. |
| T3.5.3 | `GET /v1/certificates/by-domain/{domain}` | Return all certificates observed for a given domain (subject or SAN match), and the IPs where they were seen. |
| T3.5.4 | Implement **certificate → IP correlation** | Efficient join between `CertificateObservation` and `Target`/`CurrentExposure` tables. Consider a materialised mapping table for hot lookups. |

### 3.6 Statistics & Trends Endpoints

| # | Task | Details |
|---|------|---------|
| T3.6.1 | `GET /v1/stats/ports` | Return top-N open ports by count, with service breakdown. Served from pre-computed rollup or Redis cache. |
| T3.6.2 | `GET /v1/stats/services` | Return service-type distribution across the current exposure set. |
| T3.6.3 | `GET /v1/stats/severity` | Return severity distribution: critical, high, medium, low, info. |
| T3.6.4 | `GET /v1/stats/trends` | Return time-series data: new exposures per day, closed exposures per day, total exposure count over time. Configurable date range and granularity. |
| T3.6.5 | `GET /v1/stats/changes` | Return change velocity: how many IPs changed state per day/week. |
| T3.6.6 | Implement **pre-computed aggregation refresh** | Compaction worker (Epic 2) produces rollups. Stats endpoints read from rollup tables/Redis. Refresh cadence: at least hourly. |

### 3.7 Query DSL Engine

| # | Task | Details |
|---|------|---------|
| T3.7.1 | Define **DSL grammar** | KQL-inspired. Operators: `=`, `!=`, `>`, `>=`, `<`, `<=`, `contains`, `IN (...)`, `AND`, `OR`, `NOT`. Field allowlist only — arbitrary fields are rejected. |
| T3.7.2 | Implement **DSL parser** | Parse DSL string into an AST. Return clear error messages on syntax errors with position info. |
| T3.7.3 | Implement **AST → SQL translator** | Convert the parsed AST into parameterised SQL predicates. **Never** concatenate raw values. Enforce field allowlist. Enforce maximum clause depth/count to prevent abuse. |
| T3.7.4 | Implement **AST → Redis query translator** | For simple queries (single IP, single fingerprint), translate to direct Redis key lookups instead of hitting Postgres. |
| T3.7.5 | Write **DSL reference documentation** | Grammar specification, supported fields, operators, examples. Include in OpenAPI docs. |
| T3.7.6 | Write **DSL integration tests** | Test valid queries, invalid queries, injection attempts, edge cases (empty results, max-depth queries, large IN-lists). |

### 3.8 Caching Strategy

| # | Task | Details |
|---|------|---------|
| T3.8.1 | Implement **read-through cache pattern** | On cache miss: query Postgres → return to caller → async write to Redis with TTL. |
| T3.8.2 | Implement **event-driven invalidation** | When the cache projector (Epic 2) updates a target's current-exposure data, invalidate related query caches. |
| T3.8.3 | Implement **configurable TTLs per query type** | Single-IP lookup cache: short (e.g., 60 s). Search result cache: medium (e.g., 5 min). Stats cache: longer (e.g., 15 min). All configurable via app settings. |
| T3.8.4 | Implement **cache-hit/miss metrics** | Track per-endpoint cache hit ratio. Expose as OpenTelemetry metric for dashboarding. |

### 3.9 API Documentation

| # | Task | Details |
|---|------|---------|
| T3.9.1 | Generate **OpenAPI / Swagger spec** | Auto-generate from ASP.NET Core. Annotate all endpoints with summaries, parameter descriptions, response schemas, error codes. |
| T3.9.2 | Write **Query DSL reference** | Include as a dedicated section in the API docs. |
| T3.9.3 | Write **rate limit & quota documentation** | Document per-consumer rate limits, quota behaviour, retry-after headers. |
| T3.9.4 | Provide **example requests & responses** | Curl examples and JSON response samples for every endpoint. |

---

## 4. Acceptance Criteria

1. `GET /v1/exposures/{ip}` returns correct data with p95 latency < 50 ms when served from Redis cache.
2. `POST /v1/exposures/search` returns filtered results with p95 latency < 300 ms for common queries.
3. `POST /v1/history/search` returns time-bounded results with p95 latency < 2 s using partition-pruned queries.
4. Certificate lookups correctly correlate fingerprint → IPs and domain → certificates → IPs.
5. The Query DSL correctly parses, validates, and executes at least: `ip = "1.2.3.4" AND port IN (80,443) AND lastSeen >= "2026-03-01T00:00:00Z"`.
6. The DSL rejects invalid fields, malformed syntax, and potential injection attempts.
7. All endpoints use cursor-based pagination with consistent ordering.
8. Cache invalidation triggers within seconds of underlying data changes.
9. OpenAPI spec is complete, accurate, and passes validation.
10. All endpoints return RFC 7807 problem-details responses on error.

---

## 5. Dependencies

| Dependency | Source |
|------------|--------|
| PostgreSQL schema, repository interfaces, and data-access layer | Epic 1 |
| Redis key schema and cache structure | Epic 1 |
| Shared models and telemetry bootstrap | Epic 1 |
| Cache projector populating Redis current-exposure data | Epic 2 |
| Compaction worker producing rollup / aggregate data | Epic 2 |

---

## 6. Risks

| Risk | Mitigation |
|------|------------|
| Complex DSL queries may produce slow or expensive SQL | Enforce clause-count limits, mandatory time ranges for history, query timeout, and EXPLAIN-based query plan monitoring. |
| CIDR range queries may be slow on large tables | Use PostgreSQL `inet`/`cidr` operators with GiST indexes, or a numeric IP-range approach. Benchmark at scale. |
| Cache invalidation storms on bulk data updates | Batch invalidation; use coarse-grained invalidation (e.g., per-shard) rather than per-key when appropriate. |
| OpenAPI spec drift from implementation | Auto-generate spec from code; validate in CI. |

---

## 7. Estimated Scope

| Category | Rough Effort |
|----------|-------------|
| API service foundation | Medium |
| Current-exposure endpoints | Medium |
| Search endpoints | Medium–Large |
| Historical query endpoints | Medium |
| Certificate lookup endpoints | Medium |
| Statistics & trends endpoints | Medium |
| Query DSL engine | Large |
| Caching strategy | Medium |
| API documentation | Small–Medium |

---

*This epic can begin its foundation work as soon as Epic 1's data-access layer and shared libraries are stable. Full integration requires data flowing from Epic 2.*
