# Yatra V2 — Phase 3 review

Date: 19 October 2026. Phase 3 is complete for review. Phase 4 has not started.

## Requested Phase 2 correction

Before Phase 3 work, changed only the requested generic-host status text and its affected assertions:

- `Customer selection completed.` → `Interaction completed.`
- `Comparison cancelled.` → `Interaction cancelled.`

The later changes listed below implement Phase 3 reliability. No other Phase 2 cleanup, component redesign, new component or capability discovery was undertaken. Historical `Documents/Phase-1-Review.md`, `Documents/V2-Phase-1-Review.md` and `Documents/V2-Phase-2-Review.md` are unchanged, verified against the Phase 2 source ZIP by SHA-256. Existing tests remain intact except for the explicitly requested status-text assertions.

## Behavior delivered

The generic component host now stores a versioned interaction record through the existing `yatra:` browser persistence helpers, keyed by conversation and request message ID. The record binds the complete request fingerprint to lifecycle, acknowledgement, immutable serialized submission, and correlated outcome identity. The response ULID and exact submission JSON are saved before any HTTP attempt. Retry reparses that same saved submission; it does not generate another ID, rebuild data from current controls, or change the action.

Reload during submission restores a locked, retryable interaction. Acknowledged submissions stay locked while awaiting a business outcome. Completed, cancelled, expired and failed records remain terminal across remount/reload, even when an old unmounted HTTP request finishes late. Correlated SSE completion wins over a late HTTP acknowledgement or transport result. A changed request under the old request identity and corrupt or unsupported persisted records fail closed. Fresh request/instance identities start independently. The compared customer data and submitted selection remain visible.

A browser with no component record can reconstruct submission and outcome from the existing conversation replay. Replay boundaries temporarily disable hosted actions while component history is reconstructed. Duplicate delivery preserves the same state and status text without creating another response or completion card.

The existing engine snapshot still owns inbox acceptance, pending metadata, outbox messages and terminal checkpoints. Startup validates component pending metadata against its original persisted request and private return path. Interrupted processing claims restore as pending; overdue components expire on startup. Expiry and its public request-correlated notification enter the same atomic snapshot and are delivered through the existing outbox/SSE path. Repeated cleanup or restart does not create duplicate expiry notifications.

Terminal processing failures now have a persisted `failed` pending-reply status. Completed, cancelled, expired and failed interactions reject later responses. Transient requester exceptions for durable component work leave the original inbox entry unfinished for same-identity redelivery, rather than committing a retryable error and abandoning the accepted response. V1 form exception handling remains unchanged.

## Changed files

| File | Purpose |
| --- | --- |
| `yatra-ui/src/components/ComponentHost.tsx` | Requested text corrections; durable submission, same-identity retry, monotonic outcomes, recovery and replay locking |
| `yatra-ui/src/customerComparison.test.tsx` | Only the requested completion/cancellation wording assertions changed |
| `yatra-ui/src/componentState.ts` | New focused module for versioned state validation, canonical request fingerprint, immutable submission, restoration and replay reconciliation |
| `yatra-ui/src/persistence.ts` | Add a read helper that distinguishes missing state from invalid/unreadable state; existing V1 helpers unchanged |
| `yatra-ui/src/api.ts` | Handle replay transport markers without adding conversation messages or advancing the replay cursor |
| `yatra-ui/src/App.tsx` | Pass replay progress to the generic timeline |
| `yatra-ui/src/components/MessageList.tsx` | Forward generic replay progress to the host |
| `src/Yatra.Contracts/Routing/PendingReplyStatus.cs` | Add terminal `Failed`, preserving existing enum values |
| `src/Yatra.Engine/Routing/PendingReplyResolutionStatus.cs` | Add failed resolution |
| `src/Yatra.Engine/Routing/IPendingReplyRegistry.cs` | Add failure finalization with a compatibility default for existing registry implementations |
| `src/Yatra.Engine/Routing/InMemoryPendingReplyRegistry.cs` | Implement failed finalization and rejection |
| `src/Yatra.Engine/Persistence/FileEngineStateStore.cs` | Validate restored component state; persist terminal failure; atomically queue expiry; recover overdue pending interactions |
| `src/Yatra.Engine/Workers/InboundMessageWorker.cs` | Retry interrupted durable component work and finalize explicit terminal errors safely |
| `src/Yatra.Engine/Api/YatraEngineEndpointExtensions.cs` | Emit replay transport boundaries for histories containing components |
| `tests/Yatra.Engine.Tests/ComponentRecoveryTests.cs` | 20 new engine recovery/atomicity/duplicate/corruption cases |
| `yatra-ui/src/componentRecovery.test.tsx` | 20 new browser persistence, reload, retry, ordering and corruption cases |
| `yatra-ui/src/componentTransportRecovery.test.ts` | 2 new component stream reconnect/replay-marker cases |
| `Documents/V2-Phase-3-Review.md` | This review note |

## Final validation

| Check | Result |
| --- | --- |
| `dotnet build Yatra.slnx --no-restore --nologo` | Passed, zero warnings/errors |
| `dotnet test Yatra.slnx --no-build --nologo` | 313 passed, zero failed/skipped |
| Contracts / Orchestration / Engine / AppHost | 64 / 139 / 103 / 7 passed |
| `npm test --prefix yatra-ui` | 148 passed across 11 files |
| `npm run build --prefix yatra-ui` | TypeScript check and production build passed |

All 293 Phase 2 backend tests and 126 Phase 2 frontend tests remain present and passing, including the V1 and Phase 1 suites. Phase 3 adds 20 backend and 22 frontend cases.

### Recovery evidence

| Requirement | Automated evidence |
| --- | --- |
| Rapid double action | Existing synchronous Phase 2 double-action case still passes with durable submission enabled |
| Persist before submission | Fetch assertion observes the saved immutable JSON and submitting lifecycle before transport |
| Same ID/payload retry | Timeout, remount and interrupted submission reuse byte-identical HTTP bodies; concurrent engine retries invoke the adapter once |
| Changed content conflict | Same response ID with different content returns HTTP 409, including after restart |
| Timeout after server acceptance | Browser timeout/remount retry preserves the identity; server tests seed an accepted durable inbox without history and prove HTTP replay resumes one processing path |
| Accepted is distinct from completed | Acknowledged browser records restore as submitted and locked, awaiting a correlated outcome |
| Early SSE versus late HTTP | Completion/error remain terminal after late acknowledgement; an old unmounted attempt cannot overwrite a newer host's outcome |
| Reload states | Untouched, submitting, retryable, acknowledged, completed, cancelled and expired restoration covered |
| Pending engine restart | Original request, instance, data, validator and private path survive; processing claim returns to pending |
| Accepted-response restart | Both unclaimed and claimed accepted responses resume; post-commit retries do not invoke the adapter again |
| Commit before outcome delivery | Saved outbox outcome is delivered after restart with its original identity and without another adapter invocation |
| Duplicate replay/reconnect | Component request/response delivery deduplicates; markers do not change the cursor; terminal reconstruction does not send another response |
| Terminal rejection | Completed, cancelled, expired and failed interactions reject new response identities after restart |
| Atomic failure | A failed commit leaves the saved snapshot unchanged and the accepted inbox recoverable |
| Expiry | Cleanup and notification are atomic; repeated restart/cleanup does not duplicate notification; overdue components expire during startup |
| Corrupt/unsupported state | Browser records fail closed; malformed/missing/changed engine component metadata fails startup without overwriting the snapshot |
| Phase 2 snapshot compatibility | Omitted optional description restores correctly rather than appearing corrupt |

### Manual restart check

Ran AppHost and Vite against isolated storage under `artifacts/phase3-smoke-state`:

1. Entered `compare customers` and verified the untouched comparison was interactive.
2. Stopped and restarted the engine against the same storage, then reloaded the browser. The same pending comparison and conversation returned.
3. Used keyboard Space to select C205 and Enter to confirm. Observed acceptance followed by the correlated text result and `Interaction completed.` with disabled controls.
4. Stopped and restarted the engine again, then reloaded the browser. C205 remained visibly selected, both actions remained disabled, the generic completed state remained, and the original confirmation was retained.

Proof screenshot: `artifacts/Phase3-restart-recovery.png` (separate from the source ZIP). Smoke servers were stopped after validation. Crash-window, timeout and corrupted-state scenarios were tested automatically rather than through manual timing injection.

## Design decisions and limits

- This extends the existing browser local-storage mechanism and engine atomic snapshot; no second state store was introduced. Browser records use schema version 1 and reject unsupported versions. Server snapshot version remains 1 with compatible additive enum values.
- Exact browser retry JSON is authoritative. Customer-local selection/detail-panel state is not business state; submitted selection is recovered from the immutable response. Full outcome messages remain in the existing conversation history, while the per-request record stores the outcome identity and effective lifecycle.
- The original complete request serves as validation metadata. Startup checks the persisted request/metadata binding generically; customer-specific validation stays in the orchestration validator and trusted UI registration.
- `yatra.replay` is a transport-only SSE event, emitted only for histories containing component requests. Its data uses a valid status-message envelope for tolerant SSE consumers, has no SSE cursor ID, is not persisted as a conversation message, and is consumed by the browser transport rather than displayed. Existing V1-only history framing is unchanged.
- Expiry uses a standard request-correlated `status` message with plain-text guidance and `componentLifecycle: expired`. It contains no return path, validator metadata, credentials or component properties.
- Before an atomic outcome commit, durable inbox work has at-least-once execution semantics. An adapter with external side effects must make them idempotent using the immutable response message ID; local snapshot transactions cannot atomically commit an external service. The sample adapter has no external selection/cancellation side effect. Tests prove no repeated adapter work after a successful durable commit.
- The durable host is the restart/recovery path. The in-memory registry remains useful for tests and does not promise process-restart persistence. Explicit failed finalization is supported there as well.
- When browser storage cannot save a new identity, the host sends no new response and displays a storage error. If storage becomes unavailable after acceptance, durable server replay remains authoritative; storage failure cannot provide a browser durability guarantee.
- Corrupt engine component records stop startup without replacing the original file. No automatic repair or silent metadata discard was added.
- Capabilities, a second trusted component, broad rendering/payload hardening and release-wide documentation remain Phase 4/5 work. The existing Phase 1 dependency advisory remains; dependency versions and lockfile were not changed.
- No remote execution mechanism, dynamic import, agent-supplied source/markup, authentication or unrelated workflow scope was introduced. Components still receive no network client, routing internals, credentials or lifecycle-storage API.

## Complete source delivery

`artifacts/YatraV2-Phase3.zip` contains the complete source, all V1/Phase 1/Phase 2/Phase 3 tests, fixtures, specification, historical documents and review notes. It excludes generated dependencies/build outputs, runtime state, smoke screenshots and prior archives. Every ZIP entry is verified against the source by SHA-256, and historical review documents are preserved.

Restore/build/run commands remain the same as Phase 2. Stop here for Phase 3 review; do not start Phase 4 automatically.
