# Trusted component authoring

A hosted component is trusted React code compiled with the UI. The registry is the executable allowlist. An adapter supplies declarative data and an exact contract pair, never code. This is not a sandbox or remote plugin system.

## UI registration

Implement a `HostedComponentProps` view using only title/description, instance ID, validated props, allowed actions, lifecycle/outcome and `emitAction`. Treat supplied strings as React text. Render only permitted actions and disable business controls whenever lifecycle is not ready. Use programmatic names, keyboard controls and visible state; host status/error regions provide generic announcements and validation focus.

Add one static entry in `yatra-ui/src/trustedComponents.ts`, with a statically imported view and strict validators:

```typescript
registry.register({
  componentId: 'status-summary',
  componentVersion: '1.0',
  supportedActions: ['acknowledge'],
  validateProps: value => ({ valid: validStatusSummaryProps(value) }),
  validateResponse: (action, data, props) => ({ valid: validStatusSummaryResponse(action, data, props) }),
  render: StatusSummary,
});
```

The existing status-summary entry is the minimal reference; customer-comparison demonstrates selection. Do not register either built-in twice. Exact ID/version duplicates and invalid/excessive registrations fail initialization. Discovery exports only ID/version/actions and cannot load another view. New versions coexist as independent registrations; no implicit fallback or upgrade is allowed.

The component must not receive or construct message IDs, conversation destinations, `inReplyTo`, private paths, credentials, API URLs/clients or lifecycle storage. It calls `emitAction('acknowledge', {})`; the host validates, persists identity/data, submits, retries and locks. Keep optional local view state non-authoritative. Submitted business data belongs in the response, not a hidden component store.

## Requester registration

1. Define a factory producing a fresh instance ULID and strict bounded props/actions.
2. Implement `IComponentResponseValidator` with key `componentId/componentVersion`. Validate against `PendingReply.ComponentRequest`, not client-repeated business truth. Check private requester path, identity/version/instance, permitted action, exact data fields and original request membership.
3. Register the authoritative validator in the orchestration DI extension; exactly one matching validator must exist.
4. Add the sample domain outcome entry to `SampleComponentCatalog`, plus the intent/factory publication path if demonstrating a new command. The adapter checks capabilities before requesting new UI and validates its original context again before confirming.
5. Publish an actionable component request with `expectsReply: true` and a private return path. Publish the terminal outcome with `inReplyTo` equal to the response ULID.

No concrete-component branch belongs in Engine or MessageList. Existing generic claim, persistence, expiry and replay machinery must remain reusable. The sample status extension illustrates those boundaries without introducing production `2.0` contracts.

## Validation and tests

Use exact allowed fields and conservative lengths/counts; reject unsupported versions and unknown response data. Enforce [generic and domain limits](Yatra-Version-2-Scope.md) on both client/server. Client validation is usability, never authorization. Capability declarations cannot authorize data or install a component.

Add tests for valid and malformed props, allowed actions, escaped text, keyboard access, exact response data/correlation, duplicate registration, missing capabilities, unsupported versions, double action, immutable retry and restoration. Exercise the component in the generic message list and real HTTP flow. Preserve the existing V1 suites.

## Why executable agent UI is excluded

Running agent-supplied JavaScript/HTML/modules would give untrusted output browser execution privileges, bypass audited validators and expose application state or credentials. Declarative data plus static reviewed code keeps the execution boundary explicit. Arbitrary URLs, npm installation, iframes, expressions and runtime imports are not alternative registration mechanisms in this release.
