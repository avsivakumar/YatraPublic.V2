# Yatra V2 — Phase 4 review

Date: 19 October 2026. Phase 4 is complete for review. Phase 5 has not started.

The source already present in `D:\Projects\YatraV2` at the start of this phase was the approved baseline. All existing tests, fixtures and historical review notes remain byte-identical, verified against a SHA-256 manifest captured before implementation. No prior ZIP was substituted for the working source.

## Delivered behavior

- The trusted UI registry exposes an immutable, deterministic capability list containing only exact component IDs, versions and supported action names. Both `customer-comparison/1.0` and `status-summary/1.0` are advertised.
- New user messages carry that list in the optional `payload.componentCapabilities` field through the existing HTTP/queue/message path. No new discovery endpoint, state store or executable schema is needed. The adapter checks the exact requested version and all required actions before publishing a component.
- An explicit missing capability, unsupported version, missing action, malformed list or excessive list produces correlated plain fallback text, without a pending component request. Capability declarations are advisory: they cannot register code, select an arbitrary server component, authorize data, bypass rendering validation or bypass original-request response validation.
- For compatibility with approved V1–Phase 3 behavior, a message with no capability field retains the original `customer-comparison/1.0` flow. This legacy exception does not enable the new component. The Phase 4 UI always supplies an explicit current list; an empty list never means legacy support.
- The new command `show status summary` requests trusted `status-summary/1.0`. It displays a plain-text status and summary with one `acknowledge` action. The response requires empty data and the original identity/version/instance. The existing generic host supplies persistence, immutable retry identity, correlation, replay restoration and generic completion feedback.
- Both components use the same engine pending-reply validation and queue/SSE path. Neither the engine nor `MessageList` contains a concrete component ID. The requester maintains its own exact-version domain outcome catalog; adding the extension required component code, registrations, validators, sample intent/factory and tests, rather than engine dispatch branches.

Capability representation:

```json
[
  { "componentId": "customer-comparison", "componentVersion": "1.0", "supportedActions": ["select", "cancel"] },
  { "componentId": "status-summary", "componentVersion": "1.0", "supportedActions": ["acknowledge"] }
]
```

## Version coexistence rules

1. Identity is the exact case-sensitive `(componentId, componentVersion)` pair. Canonical versions remain explicit `major.minor` decimal strings without leading zeroes. No latest-version lookup, implicit upgrade, case folding or version fallback is allowed.
2. Different versions can coexist as separate registrations with independent validators and rendering code. Duplicate pairs fail initialization. Capability discovery lists each pair separately; order is deterministic, not a version preference.
3. A request and response remain bound to the original exact pair, instance and request ULID. Advertising a newer version cannot change an existing pending interaction. Changed request data requires the existing new-identity rule.
4. Production registrations remain version `1.0` only. A test-only `1.1` proves coexistence and missing `1.2` proves no fallback. No production `2.0` contract or component was introduced.

## Payload and rendering limits

Shared server/UI checks apply to component requests/responses before generic contract validation and to advertised capability lists. Domain validators retain their stricter field rules.

| Boundary | Limit |
| --- | --- |
| Compact serialized payload | 65,536 UTF-8 bytes |
| JSON nesting | Depth 8, root at depth 0 |
| JSON value nodes, including containers | 2,048 |
| Items in any array | 100 |
| Properties in any object | 100 |
| Property-name length | 100 UTF-16 code units |
| Any string | 4,096 UTF-16 code units |
| Advertised/registered ID-version pairs | 32 |
| Actions per capability/request | 1–16 unique canonical names |
| Simultaneously hosted component views | 20 most recent component requests |

Existing identity/version/action/title/description field limits remain enforced. `customer-comparison/1.0` retains its 2–10 customer and field-length rules. `status-summary/1.0` requires exactly two non-empty text properties: `status` up to 100 characters and `summary` up to 1,000; unknown fields and non-empty acknowledgement data are rejected.

- Compact byte accounting avoids a valid payload becoming excessive solely because the engine snapshot adds indentation. A persistence/restart test covers this explicitly. The server remains authoritative for its serialization budget.
- The browser checks bounded structure before stringification, rejects cycles/non-JSON values, and rejects excessive action data before identity persistence or network submission. Invalid request properties produce the existing non-interactive unavailable card.
- The server rejects excessive responses with HTTP 400 before acceptance/history persistence. Excessive adapter output is rejected before pending registration or outbox mutation in both durable and in-memory paths. Duplicate JSON object property names also fail server validation.
- Older component requests beyond the 20-view limit display a stable non-interactive limit notice. Their engine records and browser lifecycle records are not silently completed or cancelled. Clearing the UI retains its existing semantics. V1 text and dynamic forms are not subject to the hosted-component count cap.
- These are fixed sample limits. The rendering cap bounds trusted component instances, not all timeline text/history or the runtime behavior of arbitrary React code. Only compiled trusted components can be registered.

## Changed files

Backend contracts and generic boundaries:

- `src/Yatra.Contracts/Components/ComponentCapability.cs`: minimal capability record and bounded parser.
- `src/Yatra.Contracts/Components/ComponentPayloadLimits.cs`: shared generic JSON budgets.
- `src/Yatra.Contracts/Components/ComponentPayloadValidator.cs`: apply budgets to both payload types.
- `src/Yatra.Engine/Routing/ComponentReplyMetadata.cs`: generic output validation entry point.
- `src/Yatra.Engine/Routing/EngineAgentOutputSink.cs`: reject invalid/excessive component output before registration.
- `src/Yatra.Engine/Persistence/FileEngineStateStore.cs`: apply the same check before atomic output mutation.

Requester extension:

- `src/Yatra.Orchestration/Components/StatusSummaryFactory.cs`: sample status request with fresh instance identity.
- `src/Yatra.Orchestration/Components/StatusSummaryValidator.cs`: strict properties/acknowledgement and private-path validation.
- `src/Yatra.Orchestration/Components/SampleComponentCatalog.cs`: requester-owned exact-version outcome registrations and availability checks.
- `src/Yatra.Orchestration/SampleOrchestrationAgentAdapter.cs`: capability-aware publication, fallback text and registered domain outcomes; existing customer result text preserved.
- `src/Yatra.Orchestration/Commands/SampleCommand.cs` and `SampleCommandMatcher.cs`: additive `show status summary` command, preserving prior enum values.
- `src/Yatra.Orchestration/DependencyInjection/SampleOrchestrationServiceCollectionExtensions.cs`: authoritative status validator registration.

Frontend:

- `yatra-ui/src/componentCapabilities.ts`: capability types and bounded parsing guards.
- `yatra-ui/src/componentLimits.ts`: generic browser budgets and rendering count.
- `yatra-ui/src/componentRegistry.ts`: validated registrations, registry cap and immutable exact-pair discovery.
- `yatra-ui/src/componentProtocol.ts`: payload budget guards and reusable canonical ID/version checks.
- `yatra-ui/src/components/statusSummary/StatusSummary.tsx` and `validation.ts`: trusted transport-free component and strict validators.
- `yatra-ui/src/trustedComponents.ts`: explicit static status registration.
- `yatra-ui/src/components/ComponentHost.tsx`: generic response size/shape validation before cloning/persistence.
- `yatra-ui/src/components/MessageList.tsx`: component-agnostic hosted-view count limit.
- `yatra-ui/src/App.tsx` and `types.ts`: optional capabilities on new user text.
- `yatra-ui/src/styles.css`: focused status-card styling.

Tests and review:

- `tests/fixtures/component-limits.json`: 16 shared server/UI boundary vectors.
- `tests/Yatra.Contracts.Tests/ComponentExtensionContractTests.cs`: 27 new cases.
- `tests/Yatra.Orchestration.Tests/ComponentAvailabilityTests.cs`: 21 new cases.
- `tests/Yatra.Engine.Tests/ComponentExtensibilityTests.cs`: 12 new cases.
- `yatra-ui/src/componentExtensibility.test.tsx`: 33 new cases.
- `Documents/V2-Phase-4-Review.md`: this note.

## Validation

| Check | Result |
| --- | --- |
| `dotnet test Yatra.slnx -c Release --nologo` | 373 passed, zero failed/skipped |
| Contracts / Orchestration / Engine / AppHost | 91 / 160 / 115 / 7 passed |
| `dotnet build Yatra.slnx -c Release --no-restore --nologo` | Passed, zero warnings/errors |
| `npm test --prefix yatra-ui` | 181 passed across 12 files |
| `npm run build --prefix yatra-ui` | TypeScript and Vite production build passed |

All 313 baseline backend tests and 148 baseline frontend tests still pass. This phase adds 60 backend and 33 frontend cases. Coverage includes exact capability fields, immutable discovery, legacy compatibility, absent/invalid capabilities, exact version coexistence, unknown versions, strict status validation, escaped text, acknowledgement correlation/restoration, advisory capability limitations, HTTP rejection before acceptance, atomic output rejection, shared ASCII/Unicode byte/depth/node/array/string/property boundaries, rendering limits and source assertions that the engine/timeline contain neither concrete component ID.

Production-preview smoke check: ran Release AppHost with isolated storage and served a Vite production bundle. Entered `show status summary`, acknowledged by keyboard, observed `Interaction completed.` and the correlated confirmation, then reloaded and verified the action remained disabled. Entered `compare customers` and cancelled, confirming the existing flow with explicit discovery. Screenshot: `artifacts/Phase4-status-summary.png`, delivered separately from the source ZIP. Both smoke servers were stopped. Default-base production build also passed; the preview-only API base was an environment override and is not a source change.

## Scope and delivery

No executable remote-content path, runtime module loading, agent-provided code/schema, new authentication, new state subsystem, or component-specific engine logic was added. Existing queues, private paths, persistence, retries and generic lifecycle behavior remain in use. Dependency versions, lockfile, historical documents and earlier tests are unchanged.

Release-wide documentation, manual release hardening and other Phase 5 work were not started. This remains the sample's Phase 4 review package, not a release candidate.

`artifacts/YatraV2-Phase4.zip` contains the complete source, specification, all prior/new tests and fixtures, historical documents, and review notes. Generated dependencies/build outputs, runtime storage, screenshots and earlier archives are excluded. Every entry is verified against the current source by SHA-256.

Stop for review before Phase 5.
