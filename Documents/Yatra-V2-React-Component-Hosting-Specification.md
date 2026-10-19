# Yatra V2 — React Component Hosting Specification

**Author:** Sivakumar Angarai  
**Baseline:** Yatra V1 public release (`yatrapublic-smart.zip`)  
**Working folder:** `D:\Projects\YatraV2`  
**Status:** Implementation specification  
**Implementation approach:** Complete and review one phase before starting the next

## 1. Objective

Yatra V2 extends the V1 conversation timeline so that an orchestration agent can request an approved React component, have it rendered inline in the chat interface, receive a structured human action, and continue the same correlated interaction.

V2 must preserve the complete V1 behavior. Dynamic forms remain supported and unchanged. React component hosting is an additional interaction type, not a replacement for forms.

The initial V2 release hosts only trusted React components that are compiled with the Yatra UI and registered explicitly. An agent may select a registered component and supply data, but it must never send React source, JavaScript, HTML, module URLs, or other executable content for the browser to run.

## 2. Central design principle

> The agent selects an approved component and supplies declarative data. The Yatra shell owns component resolution, validation, rendering, lifecycle, transport, correlation, retry, persistence, and failure handling.

A hosted component must not call the Yatra engine directly. It communicates only with the Yatra component host through a controlled callback. The host constructs and submits the authoritative Yatra response message.

## 3. Initial vertical slice

The first hosted component is `customer-comparison`, version `1.0`.

It displays two or more customer summaries, allows the user to inspect and select one customer or cancel, and returns a structured response. It is intentionally more interactive than a normal form and proves that Yatra can host domain-specific React experiences within the conversation timeline.

The sample command is:

```text
compare customers
```

Expected journey:

1. The browser sends the user text message.
2. The sample orchestration adapter recognizes the command.
3. The adapter publishes a `component.request` with a private return path.
4. The engine persists and delivers the request using the existing queue and SSE path.
5. The UI resolves the approved component and renders it within the message timeline.
6. The user selects a customer or cancels.
7. The host creates a correlated `component.response` using `inReplyTo`.
8. The engine resolves the private return path and routes the response to the original requester.
9. The adapter validates the response and publishes a correlated terminal text outcome.
10. The component becomes read-only and displays the completed or cancelled result.

## 4. Scope

### Included

- Two new message types: `component.request` and `component.response`
- Backend and frontend contracts for component requests and responses
- Trusted frontend component registry
- Generic hosted-component boundary
- `customer-comparison` version `1.0`
- Structured actions: `select` and `cancel`
- Server-side validation of component identity, version, instance, action, and response data
- Existing ULID identity, `inReplyTo` correlation, private return paths, queues, SSE, deduplication, persistence, replay, and restart recovery
- Browser retry identity and duplicate-submit protection equivalent to V1 forms
- Lifecycle states and restoration across remount, reload, reconnect, replay, and engine restart
- Safe handling of unknown components, unsupported versions, invalid properties, malformed responses, and expired requests
- Capability discovery for registered components after the fundamental flow is stable
- Automated contract, engine, adapter, UI, persistence, and end-to-end tests
- Updated V2 documentation and changelog

### Excluded from the initial V2 release

- Agent-generated React, JavaScript, HTML, CSS, or executable expressions
- Runtime loading of arbitrary remote modules
- NPM installation requested by an agent
- Third-party or marketplace components
- Independently deployed microfrontends
- Iframes or Web Components as a plugin mechanism
- Components making direct engine calls
- Components receiving authentication tokens, return paths, transport clients, or persistence services
- Authentication, authorization, roles, tenant isolation, approval policy, or enterprise workflow management
- Distributed or multi-instance engine coordination
- General-purpose server-driven UI schema
- Visual component designer
- File upload, document signing, or payment processing
- Token-by-token LLM streaming

## 5. Compatibility requirements

1. Every existing V1 test must continue to pass.
2. Existing V1 message meanings and serialized shapes must not change.
3. Dynamic forms must retain their current behavior, lifecycle, validation, recovery, and styling.
4. New fields must be optional unless they belong exclusively to a new V2 payload.
5. Unknown message or component types must fail safely without crashing the timeline.
6. A V1 client must not be required to understand V2 messages to preserve existing V1 contracts.
7. The engine must remain independent of React and of any particular hosted component.
8. The orchestration boundary must remain framework- and model-provider-neutral.

## 6. Canonical protocol additions

Add the following constants to `YatraMessageTypes`:

```text
component.request
component.response
```

### 6.1 Component request payload

```json
{
  "componentId": "customer-comparison",
  "componentVersion": "1.0",
  "instanceId": "01K...",
  "title": "Select the customer to continue",
  "description": "Review the matching customers and choose one.",
  "props": {
    "customers": [
      {
        "id": "C101",
        "name": "ABC Industries",
        "status": "Active",
        "location": "Dallas, TX"
      },
      {
        "id": "C205",
        "name": "XYZ Systems",
        "status": "Pending",
        "location": "Irving, TX"
      }
    ]
  },
  "allowedActions": ["select", "cancel"]
}
```

Required semantics:

| Field | Rule |
| --- | --- |
| `componentId` | Stable lowercase kebab-case identifier of a registered component |
| `componentVersion` | Explicit supported contract version; never silently upgraded |
| `instanceId` | ULID identifying this rendered component instance |
| `title` | Human-readable heading; treated as plain text |
| `description` | Optional plain-text guidance |
| `props` | JSON data validated against the selected component contract |
| `allowedActions` | Non-empty subset of actions supported by that component version |

The enclosing `YatraMessage` must use its own message ULID. `instanceId` is not a replacement for `messageId`; it identifies the component instance independently of transport delivery and replay.

For an actionable request:

- `expectsReply` must be `true`.
- The adapter must supply a private server-side return path.
- The browser must never receive or submit that return path.
- The request must be registered as pending before delivery, following the existing V1 atomicity rules.

### 6.2 Component response payload

Selection example:

```json
{
  "componentId": "customer-comparison",
  "componentVersion": "1.0",
  "instanceId": "01K...",
  "action": "select",
  "data": {
    "customerId": "C101"
  }
}
```

Cancellation example:

```json
{
  "componentId": "customer-comparison",
  "componentVersion": "1.0",
  "instanceId": "01K...",
  "action": "cancel",
  "data": {}
}
```

The enclosing response message must:

- Have a new response message ULID.
- Use `type: component.response`.
- Set `inReplyTo` to the original `component.request` message ULID.
- Repeat the original component ID, version, and instance ID.
- Contain only an action allowed by the original request.

For `customer-comparison/1.0`, `select` requires exactly one `customerId` that appeared in the original request. `cancel` requires no selection.

### 6.3 Validation boundary

Validation is layered:

- The UI validates for usability before submission.
- The generic host validates the registered ID/version and permitted action.
- The engine validates the generic message and pending-reply relationship.
- The requester or its registered validator authoritatively validates component-specific properties and response data.

Browser-supplied values, hidden values, IDs, and allowed actions are never trusted as authorization or business truth.

## 7. Frontend component architecture

### 7.1 Component registry

Create a registry that maps an exact component ID and version to a trusted registration.

Conceptual registration:

```ts
type ComponentRegistration = {
  componentId: string
  componentVersion: string
  validateProps: (props: unknown) => ValidationResult
  validateResponse: (action: string, data: unknown, props: unknown) => ValidationResult
  render: React.ComponentType<HostedComponentProps>
}
```

Resolution must use the exact pair `(componentId, componentVersion)`. Unsupported versions must not fall back to the latest version.

The registry must reject duplicate registrations at startup or test initialization.

### 7.2 Hosted component contract

A component receives only the data and callbacks required for rendering:

```ts
type HostedComponentProps = {
  instanceId: string
  title: string
  description?: string
  props: unknown
  allowedActions: string[]
  lifecycle: ComponentLifecycle
  outcome?: ComponentOutcome
  emitAction: (action: string, data: unknown) => void
}
```

The final implementation may refine TypeScript generics, but it must preserve this boundary.

The hosted component must not receive:

- API base URLs
- HTTP or SSE clients
- Message construction functions
- Return paths
- Credentials or access tokens
- Direct local-storage access for Yatra lifecycle state
- Engine persistence services

### 7.3 Generic component host

The host is responsible for:

- Parsing the payload
- Exact registry lookup
- Property validation
- Safe unsupported/error rendering
- Lifecycle display
- Immediate double-action guard
- Creating an immutable response identity and payload
- Calling the existing browser submission API
- Retry after transport uncertainty using the same response ULID and payload
- Locking the component after acknowledgement
- Applying correlated completion, cancellation, expiry, or failure outcomes
- Restoring persisted browser state
- Accessibility status announcements

### 7.4 Message-list integration

The V2 timeline supports these render paths:

```text
agent/user text
form.request
form.response
component.request
component.response
error
unsupported content
```

Do not place component-specific conditionals throughout `MessageList`. `MessageList` should delegate `component.request` to the generic component host, which then resolves the registry entry.

### 7.5 Safe failure rendering

The UI must render a stable, non-interactive error card when:

- `componentId` is missing or malformed.
- `componentVersion` is missing or unsupported.
- The component is not registered.
- Properties fail validation.
- No allowed action is supported.
- The payload is otherwise malformed.

The card may display the component ID and version as escaped plain text. It must not render supplied HTML or expose internal exceptions. Other conversation messages must continue working.

## 8. Component lifecycle

Use the following conceptual states:

| State | Meaning |
| --- | --- |
| `ready` | Valid component is interactive |
| `submitting` | User action is being submitted; controls are disabled |
| `retryable` | Submission outcome is uncertain or transport failed; retry uses the same identity and payload |
| `submitted` | Engine acknowledged the response; component is locked |
| `completed` | Correlated business outcome confirms successful completion |
| `cancelled` | Cancellation was accepted and confirmed |
| `expired` | Pending request expired and cannot be answered |
| `failed` | Terminal processing failure; component remains non-interactive unless the protocol explicitly supplies a new request |
| `unsupported` | Component ID/version or payload is unsupported |

Required rules:

1. Only `ready` may create a new response identity.
2. `submitting` must prevent rapid double submission before React rerender completes.
3. `retryable` must reuse the original response message ULID and exact serialized payload.
4. HTTP acknowledgement means accepted, not business-completed.
5. `submitted` remains distinct from `completed` and `cancelled`.
6. A correlated SSE outcome may arrive before the HTTP acknowledgement; a late acknowledgement must not regress the terminal state.
7. Replay or duplicate SSE delivery must not repeat user-visible completion or unlock the component.
8. Reload and remount must restore the same effective lifecycle.
9. An expired, completed, cancelled, or failed instance cannot accept another action.
10. A changed request must use a new message and component instance identity; previous human action must never be silently applied to changed data.

## 9. Persistence and recovery

Reuse V1 persistence mechanisms rather than introducing a second subsystem.

Server persistence must retain:

- The complete `component.request` message
- Private return path
- Expected component ID, version, instance ID, and validator identity or equivalent validation metadata
- Pending, terminal, and expiry state
- Accepted `component.response` identity and payload for deduplication
- Conversation history required for SSE replay

Browser persistence must retain, by request message ID:

- Component lifecycle
- Immutable response message ULID once an action begins
- Exact response payload used for retry
- Acknowledgement status
- Correlated outcome
- Any explicitly supported local view state that is safe to restore

Internal UI-only state such as which detail panel is expanded need not be authoritative. Business selections and submitted actions must be represented in the response message and lifecycle record.

Recovery must cover:

- Page reload before interaction
- Page reload during or after submission
- SSE disconnect and reconnect
- Duplicate SSE delivery
- Browser submission timeout after server acceptance
- Engine restart with a pending component
- Engine restart after accepting a response but before publishing its outcome
- Replay into a browser with no previous local state

## 10. `customer-comparison/1.0` contract

### Request properties

```ts
type CustomerComparisonProps = {
  customers: Array<{
    id: string
    name: string
    status: string
    location?: string
    summary?: string
  }>
}
```

Validation rules:

- `customers` must contain between 2 and 10 items.
- Every customer must have a non-empty, unique `id`.
- `name` and `status` are required non-empty plain-text values.
- `location` and `summary` are optional plain text.
- Define conservative maximum lengths in shared server and UI validation tests.
- Unknown properties should be rejected for this sample contract so mistakes fail visibly.

### UI behavior

- Render accessible customer cards in a responsive layout.
- Selection must be possible using keyboard alone.
- Show the selected customer clearly before or as part of confirmation.
- Render only actions present in `allowedActions`.
- Disable all actions immediately after one is invoked.
- After completion, retain the compared data and visibly mark the selected customer.
- After cancellation, retain the comparison as read-only and show that it was cancelled.
- Avoid modal dialogs; the interaction stays within the timeline.

### Response validation

- `select` requires one string `customerId` matching an ID in the original request.
- `cancel` requires an empty data object.
- The response ID, version, and instance ID must exactly match the pending request.
- Additional response properties are rejected.
- A response to a terminal or expired request is rejected safely.

## 11. Capability discovery

Capability discovery is introduced only after the first component flow is complete and reliable.

The browser should be able to describe registered component capabilities to the engine using a stable representation containing at least:

- Component ID
- Component version
- Supported action names

The server or adapter may use these capabilities to avoid requesting an unavailable component. Capability information is advisory, not authorization. A request must still be validated when rendered.

Do not send full JavaScript schemas, component source, or implementation details as capabilities in the initial release.

## 12. Security requirements

1. Never execute agent-supplied code, markup, styles, expressions, URLs, or module imports.
2. React text rendering must remain escaped; do not use `dangerouslySetInnerHTML`.
3. The registry is the executable-code allowlist.
4. Component ID and version matching is exact and case-sensitive after canonical validation.
5. Validate payload size, array count, field length, action count, and nesting depth at appropriate boundaries.
6. Do not trust client validation or client-repeated request data.
7. Do not expose private return paths to the browser.
8. Do not allow a component to choose message IDs, conversation IDs, destinations, sources, or `inReplyTo` values.
9. Do not provide components with credentials or direct transport access.
10. Logs must not contain full sensitive component properties or response data by default.
11. Unsupported components must fail closed.
12. Existing V1 security limitations remain explicitly documented; component hosting must not be presented as adding authentication or authorization.

## 13. Accessibility requirements

- All actions must be keyboard accessible.
- Focus must move predictably after validation failure.
- Submitting and terminal states must be announced through an appropriate live region.
- Cards and selection controls must have programmatic names and state.
- Disabled and read-only states must not depend on color alone.
- The component must remain usable at narrow mobile widths and browser zoom.
- Automated accessibility-oriented assertions should be included where practical, followed by the manual checks listed in the release phase.

## 14. Phased implementation plan

### Phase 1 — Protocol and host foundation

#### Goal

Introduce the generic V2 contracts and frontend hosting boundary without implementing the complete customer journey.

#### Work

- Add `component.request` and `component.response` message constants.
- Add backend request/response payload records and generic validation.
- Add corresponding frontend TypeScript types and parsing guards.
- Add the component registry abstraction with duplicate-registration protection.
- Add the generic component host and safe unsupported/error card.
- Route `component.request` from the message list to the host.
- Register no arbitrary or remote execution mechanism.
- Add `.gitignore` suitable for the .NET/React repository.
- Keep every existing V1 behavior and test passing.

#### Required tests

- Serialization round trips for both new payloads
- Missing/malformed required fields
- Invalid ID/version/instance/action formats
- Exact version matching
- Duplicate registry registration
- Unknown component and unsupported version rendering
- Invalid properties render safely without crashing the timeline
- Existing V1 UI and backend regression suites

#### Acceptance checkpoint

Codex must provide a phase summary listing changed files, tests added, test results, known limitations, and confirmation that no executable remote content path was introduced. Stop for review before Phase 2.

### Phase 2 — Customer comparison end-to-end slice

#### Goal

Complete one useful component interaction from user command through correlated agent outcome.

#### Work

- Implement `customer-comparison/1.0` and its exact validators.
- Register it in the trusted registry.
- Add the `compare customers` sample command.
- Publish a component request with a private return path.
- Extend pending-reply metadata so the response is bound to component ID, version, instance, and validator.
- Submit `component.response` through the existing HTTP path.
- Route the response to the original requester.
- Authoritatively validate and publish selection/cancellation outcomes.
- Implement immediate double-action protection and accessibility behavior.

#### Required tests

- Sample request factory correctness
- Component property validation boundaries
- Render two or more customers
- Keyboard selection
- Allowed-action enforcement
- Select and cancel payloads
- Response correlation through `inReplyTo`
- Wrong customer, component, version, instance, action, or return path rejection
- Multiple component requests answered out of display order
- Form and hosted component pending simultaneously without cross-routing
- End-to-end command → component → action → confirmation

#### Acceptance checkpoint

The reviewer must be able to run the sample, enter `compare customers`, select a customer, and see the correlated confirmation. Stop for review before Phase 3.

### Phase 3 — Reliability, persistence, and lifecycle

#### Goal

Bring hosted components to the same reliability level as V1 dynamic forms.

#### Work

- Persist immutable response identity and payload before submission.
- Implement retry with the same message ULID and payload.
- Separate accepted/submitted state from business completion.
- Handle early SSE outcome versus late HTTP acknowledgement.
- Persist pending and terminal component metadata in engine state.
- Restore pending and terminal states after engine restart.
- Restore UI lifecycle after remount or page reload.
- Preserve deduplication across restart and replay.
- Enforce expiry and terminal-state rejection.
- Ensure atomic state transitions follow V1 persistence rules.

#### Required tests

- Rapid double action creates one response
- HTTP retry with identical ID/payload is idempotent
- Same ID with changed content returns conflict
- Timeout after server acceptance is safely retryable
- Early SSE completion is not regressed by late acknowledgement
- Reload before action, during retry, after acknowledgement, and after outcome
- Engine restart with pending component
- Engine restart after accepted response
- Duplicate replay does not duplicate UI or adapter side effects
- Expired/completed/cancelled requests reject later responses
- Corrupt or unsupported persisted component state fails safely

#### Acceptance checkpoint

Codex must report automated recovery results and any manual restart test performed. Stop for review before Phase 4.

### Phase 4 — Capability discovery and extensibility proof

#### Goal

Prove that the design supports additional trusted components without adding component-specific logic to the engine or message list.

#### Work

- Add a minimal component-capability representation and discovery path.
- Ensure the adapter can decide whether `customer-comparison/1.0` is available.
- Add a second very small trusted component solely to prove registry extensibility; recommended: `status-summary/1.0`, read-only or with one `acknowledge` action.
- Confirm that adding it requires a registration, component, validators, and tests—not engine branching.
- Define version coexistence rules without creating version `2.0` yet.
- Add payload and rendering limits.

#### Required tests

- Capability list contains exact registered ID/version pairs
- Missing capability results in safe fallback text or supported error behavior
- Second component renders without changing generic host routing
- Registry supports multiple versions conceptually and resolves exact versions
- Oversized or excessive payloads fail safely

#### Acceptance checkpoint

Review the extension experience and confirm the engine remains component-agnostic. Stop for review before Phase 5.

### Phase 5 — Hardening, documentation, and release candidate

#### Goal

Prepare a coherent V2 sample release without overstating production readiness.

#### Work

- Run all backend and frontend tests.
- Run TypeScript checking and production build.
- Perform manual keyboard, narrow-screen, reload, reconnect, replay, and restart checks.
- Scan the release for credentials, generated output, user files, runtime storage, and unrelated artifacts.
- Update README, architecture, message protocol, developer guide, adapter guide, persistence, security, testing, troubleshooting, user guide, V2 scope, and changelog.
- Preserve V1 historical documents instead of rewriting them as if V2 existed earlier.
- Document component authoring and registration.
- Document why executable agent-supplied UI is excluded.
- Create a clean V2 source archive only after review.

#### Release acceptance

- All V1 and V2 automated tests pass.
- Backend build and frontend production build pass.
- No credentials or local-only files are present.
- Documentation matches actual behavior.
- V2 is described as an architectural sample, not a production agent platform.
- Known limits and security boundaries are explicit.

## 15. Implementation rules for Codex

1. Work only in `D:\Projects\YatraV2`.
2. Treat the extracted public V1 release as the compatibility baseline.
3. Complete only the requested phase; do not begin later phases automatically.
4. Inspect the existing implementation before designing replacements.
5. Reuse V1 message, queue, routing, persistence, SSE, retry, and testing patterns.
6. Prefer additive changes and small focused files.
7. Do not refactor unrelated V1 code unless necessary; explain any necessary refactor.
8. Do not remove or weaken existing tests.
9. Add tests with every behavioral change.
10. Never introduce runtime execution of agent-supplied code or markup.
11. Do not add authentication, workflow, microfrontend, or marketplace scope implicitly.
12. Do not add a new state store when the V1 mechanism can be extended.
13. Keep the engine free of React and component-specific rendering logic.
14. Keep components free of Yatra transport construction and direct network submission.
15. At the end of each phase, run available backend tests, frontend tests, and builds; clearly state anything that could not be run.
16. Deliver a ZIP containing the complete phase result, not only changed files.
17. Include a concise review note with changed files, new behavior, tests, limitations, and any design decision that differs from this specification.

## 16. Final architectural boundary

| Responsibility | Agent/adapter | Yatra engine | Yatra UI host | Registered component |
| --- | --- | --- | --- | --- |
| Choose interaction intent | Yes | No | No | No |
| Request approved component and provide data | Yes | Transports and persists | Resolves | Receives validated data |
| Hold private return path | Supplies | Yes | No | No |
| Render React | No | No | Hosts | Yes |
| Construct Yatra response message | No | Validates/routes | Yes | No |
| Emit domain action | No | No | Receives | Yes |
| Validate generic protocol | Initial construction | Yes | Yes | No |
| Validate domain response authoritatively | Yes | Routes validator context | Usability validation | Optional local validation only |
| Retry/deduplicate/persist interaction | Idempotent side effects | Yes | Browser retry state | No |

## 17. Definition of V2 success

Yatra V2 succeeds when a developer can add an approved, domain-specific React component to the registry; an agent can request it using a stable versioned contract; a person can interact with it naturally inside the conversation timeline; and Yatra can correlate, validate, persist, retry, recover, and complete that interaction without exposing routing internals or executing agent-supplied code.

