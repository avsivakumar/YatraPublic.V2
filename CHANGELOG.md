# Changelog

## V2 sample release candidate — 2026-10-19

### Added

- Trusted inline React hosting alongside all V1 dynamic forms, with exact versioned request/response contracts and a static allowlist registry.
- Customer comparison select/cancel and status summary acknowledge contracts, both `1.0`.
- Advisory browser capability discovery, safe fallback, strict domain validators and generic payload/rendering limits.
- Host-owned immutable response identity, browser recovery, correlated terminal lifecycle, engine restart/atomicity/expiry and replay tests.
- Current V2 architecture, protocol, developer/component/adapter, persistence, security, testing, troubleshooting, user and scope guides.
- Exact public V1 documentation snapshot under `docs/v1`, preserving original dates and release wording.

### Release hardening

- Patch the transitive `source-map-js` advisory by locking 1.2.2, without upgrading direct dependencies.
- Retain a single pending SSE read/heartbeat across idle intervals and cancel it on disconnect; add delivery/cursor-resume coverage.
- Make pre-cancelled durable inbox/outbox reads throw cancellation consistently; preserve the existing atomic-completion regression test.
- Correct local startup documentation to the existing HTTPS Vite target and document an HTTP-only alternative.
- Source-only package verification and explicit manual keyboard, narrow-screen, reload, replay, reconnect and restart evidence in the Phase 5 review.

V2 is an architectural sample, not a production agent platform. Component versions remain `1.0`; there is no executable agent UI, authentication, tenant isolation, distributed engine or exactly-once external transaction guarantee. No public publication is performed by preparing this candidate.

## Historical V1 record

The original V1 entries below retain their historical "Unreleased" and development-phase wording. They describe the original sample, not the V2 phase plan. The complete [original changelog](docs/v1/CHANGELOG.md) and [V1 documentation snapshot](docs/V1-History.md) remain available.
## [Unreleased] - Version 0.1 / Version 1 sample

### Added

- React conversation UI and ASP.NET Core engine
- HTTP browser-to-engine submission and SSE engine-to-browser delivery
- ULID message and conversation identity
- Asynchronous inbound and outbound queues
- Framework-neutral orchestration adapter boundary
- Dynamic forms with text, textarea, number, date, datetime, select, multiselect, checkbox, display, and hidden fields
- Client and authoritative server validation
- Pending-reply correlation with server-side return paths
- JSON persistence for queues, pending replies, and conversation replay
- Browser draft, retry-identity, outcome, composer, and view-state persistence
- Fake LLM and optional OpenAI client
- Local Clear all messages feature
- Architecture, protocol, adapter, developer, persistence, security, testing, user, scope, philosophy, overview, and use-case documentation

### Reliability hardening

- Fail-closed validation for unsupported and malformed form definitions
- Draft preservation and safe resubmission after transport failure
- Immediate UI submission guard and serialized server acceptance
- Idempotent retries using the original response ULID and payload
- Conflict response when an existing ULID is reused with different content
- `submitted` state separated from correlated `completed` and `cancelled` outcomes
- Early SSE outcomes protected from late HTTP acknowledgement races
- Bounded SSE reconnection, single active stream, cursored replay, and ULID deduplication
- Exhaustive server validation for all Version 1 field types and constraints
- Atomic engine snapshot, interrupted-claim recovery, and restart tests

### Known limits

- Single process per storage folder
- At-least-once adapter invocation across crash boundaries
- No authentication, authorization, tenancy, distributed coordination, or production database provider
- Complete-message agent responses; token-by-token LLM streaming is deferred

## Development phases

- **Phase 1:** established the browser, engine, basic messaging, and initial review baseline.
- **Phase 2:** developed asynchronous browser-to-engine and engine-to-browser communication.
- **Phase 3:** introduced message identity, correlation, routing, and adapter boundaries.
- **Phase 4:** completed dynamic-form interaction and orchestration integration.
- **Phase 5:** completed the end-to-end architectural sample, followed by Smart 7 and Smart 8 reliability, validation, lifecycle, persistence, and UI hardening.

The phase summary is intentionally architectural; Git history should be used for file-level change details after publication.

