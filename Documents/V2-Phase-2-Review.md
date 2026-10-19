# Yatra V2 — Phase 2 review

Date: 19 October 2026. Phase 2 is complete for review. Phase 3 has not started.

## Document preservation

Before Phase 2 implementation, restored `Documents/Phase-1-Review.md` directly from `D:\Projects\yatrapublic-smart.zip` and renamed the V2 Phase 1 review to `Documents/V2-Phase-1-Review.md`, without changing its contents. Verified exact byte equality by SHA-256:

- Restored V1 review: `877CA3F0BD4B25AFD80F8B9D231B7F20163AA74E9CD8EC510E6AB81A34970BCE`.
- Renamed V2 Phase 1 review: `4441ECEFE7D4693DC1C1971E2ED7513A68917CEBBF725A736C6D6B34D780A616` (matches the review in the Phase 1 ZIP).

No unrelated Phase 1 code changes or changes to existing V1/Phase 1 tests were made. Existing source edits listed below are required extensions for Phase 2.

## Behavior delivered

Enter `compare customers` in the sample UI. The sample orchestrator publishes `customer-comparison/1.0`, containing ABC Industries (C101) and XYZ Systems (C205), with a fresh instance ULID and independent request message ULID. The existing engine registers the private return path before delivery and sends only the public message through SSE.

The trusted component displays responsive customer cards, keyboard-accessible radio selection, optional expandable details, confirmation, and cancellation. Only allowed actions appear. The generic host validates actions, constructs the response identity and correlation, prevents synchronous double actions, submits through the existing HTTP API, locks acknowledged interactions, and applies correlated selection/cancellation/error outcomes. Selection remains visible in the read-only comparison. Status changes use a polite live region; invalid-action feedback receives focus.

Pending metadata retains the original component request (identity, version, instance, allowed actions, and properties) and an exact validator key. The engine checks the generic response against this original metadata. A registered orchestration validator checks exact domain data, including membership of the chosen customer in the original request. Missing or ambiguous authoritative validator registrations fail closed. Form and component responses cannot consume each other's pending requests.

The adapter independently validates the resolved private return path and original request before publishing a terminal text outcome whose `inReplyTo` is the response message ULID. Server outputs remain component-agnostic in the engine; domain code lives in orchestration and the trusted UI registration.

## Changed files

Document corrections and delivery note:

- `Documents/Phase-1-Review.md`: restored V1 document.
- `Documents/V2-Phase-1-Review.md`: unchanged V2 Phase 1 document under its new name.
- `Documents/V2-Phase-2-Review.md`: this note.

Backend additions:

- `src/Yatra.AgentIntegration/IComponentResponseValidator.cs`: framework-neutral authoritative validator interface.
- `src/Yatra.Engine/Routing/ComponentReplyMetadata.cs`: generic component request parsing and exact validator key.
- `src/Yatra.Orchestration/Components/CustomerComparisonFactory.cs`: deterministic customer data with fresh instance identity.
- `src/Yatra.Orchestration/Components/CustomerComparisonValidator.cs`: strict property and response validators.

Backend extensions:

- `src/Yatra.AgentIntegration/AgentInput.cs`: optional original component request context for the requester.
- `src/Yatra.Contracts/Routing/PendingReply.cs`: optional original component request metadata.
- `src/Yatra.Engine/Api/YatraEngineEndpointExtensions.cs`: allow browser component responses through the existing endpoint; preserve the legacy error-message phrase used by V1 tests.
- `src/Yatra.Engine/Routing/EngineAgentOutputSink.cs`: register component metadata before in-memory delivery.
- `src/Yatra.Engine/Persistence/FileEngineStateStore.cs`: include the same metadata in the existing atomic output registration; reuse existing completion/cancellation checkpoints.
- `src/Yatra.Engine/Workers/InboundMessageWorker.cs`: claim, bind, validate, dispatch and finalize component responses; reject cross-type replies.
- `src/Yatra.Orchestration/Commands/SampleCommand.cs`: additive command, preserving existing enum numeric values.
- `src/Yatra.Orchestration/Commands/SampleCommandMatcher.cs`: command matching.
- `src/Yatra.Orchestration/SampleOrchestrationAgentAdapter.cs`: request publication and authoritative correlated outcomes.
- `src/Yatra.Orchestration/DependencyInjection/SampleOrchestrationServiceCollectionExtensions.cs`: domain validator registration.

Frontend additions:

- `yatra-ui/src/components/customerComparison/validation.ts`: strict customer property/response validators.
- `yatra-ui/src/components/customerComparison/CustomerComparison.tsx`: trusted, transport-free customer UI.
- `yatra-ui/src/trustedComponents.ts`: static registration for version 1.0.

Frontend extensions:

- `yatra-ui/src/componentRegistry.ts`: install the trusted registration.
- `yatra-ui/src/componentProtocol.ts`: optional action/data outcome information for read-only selection rendering.
- `yatra-ui/src/components/ComponentHost.tsx`: host-owned submission, immediate guard, outcome correlation and accessible status/error feedback; preserve inert payload-only Phase 1 preview behavior.
- `yatra-ui/src/components/MessageList.tsx`: pass generic request/history context to the host, without customer-specific branches.
- `yatra-ui/src/api.ts`: listen for the two component SSE event types.
- `yatra-ui/src/types.ts`: permit component responses in browser submissions.
- `yatra-ui/src/styles.css`: responsive customer cards and visible selection/focus styling.

Tests:

- `tests/fixtures/customer-comparison-props.json`: 30 shared server/UI property cases, including count, required fields, maximum lengths, uniqueness, nulls, and unknown fields.
- `tests/Yatra.Orchestration.Tests/CustomerComparisonTests.cs`: 43 new cases covering shared property boundaries, factory identity, command publication, select/cancel correlation, forged identity/action/customer data, private return-path rejection and extra-field rejection.
- `tests/Yatra.Engine.Tests/ComponentFlowTests.cs`: 14 new cases covering real HTTP/queue/adapter/SSE select/cancel flow, metadata registration, public SSE return-path isolation, forged responses without consuming requests, replies in reverse order, simultaneous forms/components, cross-routing rejection, missing validators and wrong private paths. Both durable and in-memory flows are exercised.
- `tests/Yatra.Engine.Tests/Yatra.Engine.Tests.csproj`: test-only reference to orchestration for full-flow tests; the engine production project has no orchestration reference.
- `yatra-ui/src/customerComparison.test.tsx`: 43 new cases covering shared property boundaries, response data, rendering, keyboard selection, select/cancel payloads, correlation, allowed actions, immediate double-action prevention, validation focus, retained read-only data and the App command/SSE/HTTP/outcome flow.

## Final validation

| Check | Result |
| --- | --- |
| `dotnet build Yatra.slnx --no-restore --nologo` | Passed, zero warnings/errors |
| `dotnet test Yatra.slnx --no-build --nologo` | 293 passed, zero failed/skipped |
| Contracts / Orchestration / Engine / AppHost | 64 / 139 / 83 / 7 passed |
| `npm test --prefix yatra-ui` | 126 passed across 9 files |
| `npm run build --prefix yatra-ui` | TypeScript check and production build passed |

All 236 Phase 1 backend tests and 83 Phase 1 frontend tests remain present and passing, including the V1 regression suites. Phase 2 adds 57 backend and 43 frontend cases.

Real browser smoke check: ran AppHost with isolated storage and the Vite UI; typed `compare customers`, used Space to select C101 and Enter to confirm, observed the correlated confirmation and disabled controls. Requested another comparison, cancelled it, and observed the correlated cancellation and retained read-only data. The browser viewport was approximately 481 pixels wide; cards remained usable in a single column. Screenshots are saved separately under `artifacts/Phase2-selection.png` and `artifacts/Phase2-cancellation.png`. Smoke-test servers were stopped after verification.

## Decisions, boundaries and limitations

- Component property limits match across server/UI: 2–10 customers, ID 100 characters, name 200, status 100, optional location 200 and summary 1000. Required strings cannot be whitespace; IDs are unique and case-sensitive. Optional values must be strings when supplied. Unknown property and response fields are rejected; cancel data must be empty and select data must contain exactly one original customer ID.
- The complete original request is the validation metadata, rather than duplicating separate expected ID/version/instance fields. Validator identity is the exact `componentId/componentVersion` pair. These are private engine records, never browser routing data.
- Existing server state and atomic commit mechanisms naturally store the new optional metadata. This is required for the normal Phase 2 flow in the existing durable host, not a new state subsystem. Existing optional metadata remains readable for V1 records.
- The host retains a response draft in memory and offers same-identity retry after uncertain transport failure. Durable browser draft persistence, comprehensive remount/reload/replay/restart recovery validation and corruption handling remain Phase 3 work. Do not treat Phase 2 as full V1-equivalent reliability for hosted components.
- Basic accepted/submitted versus correlated completion display is necessary for this slice. Full lifecycle hardening and the Phase 3 recovery matrix are not claimed complete.
- Existing completion checkpoints use the V1 submit/cancel action discriminator internally; component select maps to successful completion and cancel to cancellation. No public V1 payload shape changes were made.
- Capability discovery, a second component, broad payload/depth hardening and release-wide documentation are deferred to their specified phases. Historical V1 documentation is preserved.
- Full browser zoom/screen-reader and recovery/restart manual checks were not performed. Automated keyboard/focus/status assertions and the narrow browser smoke check passed.
- The existing Phase 1 `source-map-js` transitive dependency advisory remains documented; dependencies and the package lockfile were not upgraded in this phase.
- No authentication/authorization features were added. This remains an architectural sample.
- No executable remote-content path was introduced: no agent source, markup, expressions, remote modules, eval or dynamic imports. The customer component has no transport construction, network client, credentials, private return path or direct lifecycle storage access.

## Reproduce and deliver

From the repository root, restore with `dotnet restore Yatra.slnx` and `npm ci --prefix yatra-ui`. Start the sample with `dotnet run --project src/Yatra.AppHost --launch-profile https`; start Vite with `npm run dev --prefix yatra-ui`, then open `http://localhost:5173`. The existing Vite proxy targets `https://localhost:7144`, so use the HTTPS AppHost launch profile and an already trusted local development certificate. The default fake LLM requires no API key. Enter `compare customers`, select a radio option, and confirm, or cancel.

`artifacts/YatraV2-Phase2.zip` contains the complete source, tests, shared fixtures, specification, historical documents and both V2 review notes. Generated dependencies, build outputs, runtime state, screenshots and older archives are excluded. Archive entries are checked against the source, including the two preserved review-document hashes.

Stop here for Phase 2 review. Do not start Phase 3 automatically.
