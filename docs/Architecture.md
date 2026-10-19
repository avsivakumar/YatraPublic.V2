# Yatra V2 architecture

Yatra V2 adds trusted hosted React interactions alongside the complete V1 dynamic-form path. The engine remains independent of React, concrete components, and model providers. This is a single-process architectural sample.

## Ownership

| Layer | Owns |
| --- | --- |
| Adapter/requester | Intent, approved contract selection, domain data, private return path, authoritative domain validation, correlated outcome |
| Engine | Trusted envelope, inbox/outbox, pending claims, generic validation, correlation, expiry, deduplication, atomic state, SSE/history |
| Generic UI host | Exact registry lookup, property validation, lifecycle, immutable response construction, HTTP, retry, browser storage, error boundary |
| Trusted component | Accessible display and local view state; calls `emitAction(action, data)` |

A hosted component receives instance/title/description, validated props, allowed actions, lifecycle/outcome and a callback. It receives no transport client, credentials, internal path, message constructor or lifecycle storage API. The host determines the request correlation and response identity.

## Projects

`Yatra.Contracts` contains protocol records and generic limits; `Yatra.AgentIntegration` contains adapter/validator interfaces; `Yatra.Engine` contains infrastructure; `Yatra.Orchestration` contains sample domain behavior; `Yatra.AppHost` composes them. `yatra-ui` owns React rendering. Architecture tests prevent engine references to concrete orchestration and concrete component IDs in the engine/timeline.

## Runtime flow

1. The browser opens conversation SSE and posts `user.text`, advertising its exact compiled component capabilities.
2. The engine assigns trusted routing/timestamps and durably accepts the message before HTTP acknowledgement.
3. The adapter requests a supported form or component, or publishes fallback text. An actionable request has its own message ULID, and a component also has an independent instance ULID.
4. Private pending metadata is registered atomically with the outbound request before delivery. The browser sees only the public envelope.
5. `MessageList` delegates all component requests to the generic host, which resolves the exact ID/version registration and validates its data. Unknown, malformed or excessive content becomes a non-interactive card.
6. A human action causes the host to save an immutable response ULID and serialized submission before HTTP. Retry uses the same identity/data; acknowledgement means submitted, not completed.
7. The engine claims the original pending request using `inReplyTo`, rejects terminal/expired or cross-type replies, checks identity/version/instance/actions and calls exactly one registered authoritative component validator.
8. The adapter verifies its private path and original request context, then publishes an outcome correlated to the response ULID. The engine commits outputs and terminal state with input completion. The host applies a monotonic completed/cancelled/failed outcome.

The V1 form path retains its existing value validation and explicit-rejection resubmission rules; component response data is never silently edited and resubmitted as a new action.

## Discovery and extensibility

The UI sends only ID, version and supported actions. An explicit unavailable/malformed capability list produces fallback text. Absent capability metadata retains the approved legacy customer-comparison behavior; new status-summary requires explicit support. Capability claims cannot register executable code or authorize a response.

The UI registry and requester domain catalog both use exact pairs. Only version `1.0` of each sample component is registered. Coexisting future versions need separate registrations and validators; there is no latest-version fallback. See [component authoring](Component-Authoring.md).

## Persistence, replay and limits

The atomic JSON snapshot owns inbox, outbox and pending metadata. Conversation history is projected before delivery. Interrupted claims recover; committed inputs do not invoke the adapter again. Unfinished input is at least once: external effects must deduplicate by input ID. Durable terminal records and accepted identities are retained.

SSE is conversation-scoped, has bounded subscriber buffers and disconnects slow readers. Named application `error` events are distinct from transport failures. Browser reconnect uses bounded exponential backoff, a cursor and deduplication. Component replay markers lock actions during history reconstruction. The SSE loop retains one pending channel read and one heartbeat timer through idle periods; closing the stream cancels both.

Generic payload, registry and hosted-view limits are defined in [V2 scope](Yatra-Version-2-Scope.md). They do not add a general server-driven UI schema or sandbox for arbitrary component code.

No authentication, tenant isolation, distributed coordination or production retention policy is provided. See [security](Security.md) and [persistence](Persistence.md).
