# Troubleshooting — Yatra V2

## UI cannot connect

The existing Vite proxy targets `https://localhost:7144`, so use `dotnet run --project src/Yatra.AppHost --launch-profile https`. For the HTTP profile, set UI `VITE_YATRA_API_BASE_URL=http://localhost:5130` before starting/building it. The exact UI origin must be allowed by AppHost CORS. A production bundle needs a configured API base or same-origin proxy. See [README](../README.md).

## Text is disabled or connection is lost

New text waits for connected SSE. Automatic retries use bounded backoff; after exhaustion choose **Retry connection**. Existing form/component HTTP may still submit while delivery reconnects. Hosted controls disable during replay reconstruction. Check the API origin, server process and stream request rather than repeatedly submitting a choice.

## Component unavailable or capability fallback

Check exact ID/version/action names in the trusted registry and advertised capability list. No latest-version fallback exists. An explicit missing/malformed list produces safe text. Absent metadata permits the legacy customer comparison only. Property shape, field lengths, generic JSON limits and unknown fields can also reject a card. Do not send source/HTML/module URLs to fix an unsupported component.

## Rendering limit notice

Only the 20 most recent component requests are hosted. Older cards become non-interactive notices; their server state is not cancelled. The sample does not provide an interaction manager for reopening hidden older pending cards. Wait for a new request or use a fresh demonstration conversation as appropriate; local clearing is not server deletion.

## Interaction remains submitted or shows retry

Submitted means accepted, not business-completed. Check the adapter, validator key, original request metadata, outbox and SSE. Retry after uncertain transport reuses the same saved identity/data. Terminal completed/cancelled/failed/expired interactions stay locked. Do not edit local records or reuse an old request ID with changed props.

## HTTP 409 or validation rejection

409 indicates an ID reused with different content. A component transport retry must preserve exact original action/payload; it cannot choose another customer. A new human decision requires a new request. V1 form explicit-rejection replacement behavior remains separate. Generic excessive/malformed component input returns 400 before acceptance; domain-invalid queued input receives a correlated error.

## Missing drafts or storage errors

Local state is origin/profile-scoped. A changed port, restrictions, quota or cleared site data can remove/isolate it. Initial component identity saving must succeed before network submission. Browser clearing does not erase engine history. Corrupt/changed-request component records fail closed; preserve evidence and issue a new request rather than silently unlocking them.

## Restart or file-lock failure

Use the same `Yatra:StorageFolder`, keep it writable/protected, and run only one owner. Stop the smoke server before rebuilding on Windows to avoid locked DLLs. Corrupt snapshots stop startup; preserve the file and backup rather than replacing it with empty state. Default Debug/Release storage paths differ because they are relative to the executable.

## Forms and model failures

Forms retain their original definition/value rules; compare with [protocol](Message-Protocol.md). Use Fake to isolate transport from an external model issue. The optional OpenAI sample uses private constants and embeds a populated key; never paste a key into an issue or shared archive. Record versions and safe IDs/error codes, not full sensitive properties.

## Clean verification

Run the exact [testing commands](Testing.md), beginning with `npm ci` when checking the lockfile. Historical documents describe their original versions; use current guides for V2. The release review lists verified checks and known limits.
