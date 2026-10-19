# Testing and release verification

From the repository root:

```powershell
dotnet test .\Yatra.slnx -c Release
dotnet build .\Yatra.slnx -c Release --no-restore
npm ci --prefix .\yatra-ui
npm test --prefix .\yatra-ui
npm run build --prefix .\yatra-ui
npm audit --prefix .\yatra-ui
dotnet list .\Yatra.slnx package --vulnerable --include-transitive
```

The UI build checks TypeScript before generating production assets. Tests use Fake/injected model clients and do not call paid model APIs. Use a unique temporary `Yatra:StorageFolder` per host instance and dispose it before starting another owner.

## Coverage

| Area | Evidence |
| --- | --- |
| V1 compatibility | Existing form fields, validation, retry/replacement, drafts, API, routing and AppHost suites preserved |
| Contracts | Message/instance ULIDs, payload shapes, serialization, limits, capabilities and published examples |
| Host | Exact versions, duplicate registration, unavailable/error cards, allowed actions, keyboard/focus, immediate double-action guard |
| Components | Customer select/cancel, status acknowledge, strict domain data, escaped strings and generic routing |
| Reliability | Persist-before-send, same-identity timeout retry, HTTP conflict, early SSE/late HTTP races and old unmounted callbacks |
| Recovery | Browser reload/remount, fresh-state replay, terminal lock, pending/accepted/committed server restart, corruption and atomic failure |
| Streaming | Named events, cursor resume, deduplication, reconnect/exhaustion, heartbeat after idle, bounded subscribers and replay markers |
| Extensibility | Exact capability pairs, fallback, version coexistence and no component-ID branch in engine/timeline |

New component changes need both client/server validation, generic timeline/HTTP integration and recovery checks. Keep V1 tests intact. Current counts and release-scan results are recorded in [Phase 5 review](../Documents/V2-Phase-5-Review.md), rather than rewriting historical verified dates.

## Manual release checks

Run the Release host and a production Vite preview with an explicit API base and exact CORS origin. Use isolated storage. Verify:

1. Text and all form/component commands; required-field errors and predictable keyboard focus.
2. Customer selection/confirmation and cancellation; status acknowledgement; generic terminal text and disabled controls.
3. Narrow widths and zoom; visible labels/selection without color-only meaning; keyboard-only Tab/Space/Enter/arrow navigation.
4. Form draft reload, component reload before action, after acknowledgement/outcome, and duplicate-free replay.
5. Engine stop/restart with pending work; reconnect indicator and eventual recovery; completed state remains locked after another restart/reload.
6. SSE cursor resume and fresh history replay without duplicate cards or repeated business effects.
7. Local message clearing versus server persistence.

Timeout/crash-window, corrupt-state and quota tests are deterministic automated tests; do not claim a manual check was performed unless recorded. Screen-reader behavior is not certified by jsdom tests. The release review distinguishes actual browser checks from automated scenarios and remaining limits.

## Package verification

Archive reviewed source only; inspect every entry for runtime/user/generated files. Scan source/configuration for credentials without printing matched values. Verify known placeholders and Fake default. Compare archived bytes to the final source, test the archive's own source after extraction, and preserve V1 snapshots and earlier reviews. Record advisory tool failures explicitly rather than treating unavailable scans as clean.
