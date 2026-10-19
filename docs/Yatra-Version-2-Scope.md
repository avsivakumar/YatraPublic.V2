# Yatra V2 scope

V2 is an architectural sample extending V1 chat/forms with compiled, explicitly registered React components. V2 is a project iteration, not a silent upgrade to component contract version `2.0`. Both built-in contracts remain `1.0`.

## Included

- Original V1 message/form behavior and all field types.
- `component.request` and `component.response`, independent instance identity, exact registry resolution and safe failure cards.
- `customer-comparison/1.0` (`select`, `cancel`) and `status-summary/1.0` (`acknowledge`).
- Advisory browser capabilities with exact versions/actions and safe fallback.
- Generic host ownership of HTTP, persisted retry identity, lifecycle and outcome correlation.
- Server original-request binding, authoritative validators, private return paths and cross-type rejection.
- Atomic file inbox/outbox/pending state, restart recovery, history replay, deduplication and expiry notification.
- Automated regression/recovery/extension tests, production builds and documented manual checks.

## Fixed component limits

| Limit | Value |
| --- | --- |
| Compact serialized JSON | 65,536 UTF-8 bytes per component payload/capability list |
| Nesting | Depth 8, root depth 0 |
| JSON value nodes | 2,048 including containers |
| Array items / object properties | 100 each |
| Property-name length | 100 UTF-16 code units |
| Any string | 4,096 UTF-16 code units |
| Registered/advertised ID-version pairs | 32 |
| Actions | 1–16 unique canonical names |
| Concurrent hosted component views | 20 most recent requests |
| ID / version / action length | 100 / 32 / 64 |
| Title / optional description length | 200 / 2,000 |

Canonical IDs/actions are lowercase kebab-case; component versions are explicit `major.minor` strings without leading zeroes. Matching is exact and case-sensitive. Server serialization is authoritative for its byte budget. Duplicate JSON object properties fail server validation.

Customer comparison requires 2–10 customers, unique non-empty IDs (100 characters), name (200), status (100), optional location (200) and summary (1,000). Unknown fields are rejected. Select data contains exactly one original customer ID; cancel data is empty.

Status summary requires exactly two non-empty plain-text properties: status (100) and summary (1,000). Acknowledge data is empty. No other action is supported.

Older component views beyond the count limit show a non-interactive notice. Their pending records are not silently cancelled. V1 text/forms remain unaffected by the component-view cap. Full timeline/history storage is not bounded by this rendering cap.

## Excluded and remaining limits

No agent-generated source/HTML/CSS/expressions, arbitrary modules, runtime package installation, marketplace, microfrontends, iframes as plugins, visual designer, payment, signing, uploads or token streaming. No authentication, authorization, roles, tenancy, approval workflow, production secret provider, distributed engine or database provider.

Only one process owns a storage folder. History/checkpoint files and browser storage are local and unencrypted by the sample. Accepted IDs/terminal state have no automatic pruning; disk and browser quota must be managed. Corrupt engine snapshots stop startup; corrupt component browser records fail closed. External side effects before a crash may repeat and require idempotency. Fixed payload limits do not sandbox compiled trusted code or constitute comprehensive request/abuse controls.

Historical V1 scope is preserved [separately](Yatra-Version-1-Scope.md). See [security](Security.md) before adapting this sample to a real deployment.
