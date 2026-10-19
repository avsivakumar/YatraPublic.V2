# Persistence and recovery — Yatra V2

The existing single-process JSON store owns V1 forms and V2 components. Set `Yatra:StorageFolder` to a stable protected directory. The default is `library` beneath the host executable directory, which can differ between Debug/Release builds.

| Location | Contents |
| --- | --- |
| `engine-state.json` | Durable inbox/outbox messages, accepted identities, processing/delivery checkpoints and pending/private routing metadata |
| `conversations/{conversationId}.messages.json` | Public message history for cursor replay |
| `engine-state.lock` | Exclusive process ownership; released by the OS on exit |
| Browser `yatra:*` keys | V1 form state, component records, drafts, outcomes, composer, hidden-message IDs and conversation cache |
| Browser `yatra.conversationId` | Current conversation ULID |

## Atomic server boundaries

HTTP commits the inbox before acknowledgement; history is a separate projection. The worker writes recovered input history before processing it. Accepted identities/payloads remain for deduplication; same ID with changed type/correlation/content conflicts.

Adapter outputs are staged, then committed with input completion and pending terminal state. New requests and their private return paths are registered with their outbound messages before delivery. The outbound worker projects history before SSE and marks delivery afterward. A crash in that window can redeliver the same ID; clients deduplicate it.

Snapshot changes use temporary-file write, disk flush and atomic replacement. Failed commits do not replace live/durable state. Startup restores unfinished queues and resets interrupted processing claims. Terminal records remain terminal. Component metadata is checked against its original request, validator pair and private path; corrupt or unsupported state stops startup without replacing the file.

Overdue components expire at startup. Cleanup and lazy expiry atomically enqueue one public request-correlated expiry status, without private metadata. Repeated cleanup/restart does not create another expiry notification. The in-memory registry supports tests but does not provide process-restart persistence.

## Browser component recovery

A versioned record at `yatra:component:{conversationId}:{requestMessageId}` binds the complete request fingerprint to lifecycle, immutable serialized submission, acknowledgement and correlated outcome identity. The exact response ULID/data is saved before the first HTTP attempt. An interrupted submitting record restores as retryable and locked; Retry sends the same saved JSON. It cannot silently apply an old selection to changed request data.

Acknowledgement restores as submitted, awaiting business confirmation. Completed, cancelled, failed and expired records remain read-only after reload. An early SSE outcome takes precedence over a late HTTP success/failure, including a callback from an unmounted host. A browser with no local record reconstructs accepted responses and outcomes from replay. Corrupt/unsupported records fail closed. Component-local panel expansion and unsent radio focus are not authoritative submitted business state.

V1 form drafts and explicit-rejection replacement semantics remain unchanged. Forms and components use their own original request identities, so pending interactions cannot cross-route.

## Replay and reconnect

SSE sends history before live messages, with cursor resume and ULID deduplication. Component replay markers temporarily lock hosted actions during reconstruction. Cached display order can reflect when missing messages were appended to the browser cache; lifecycle/correlation uses IDs, not visual position. Duplicate delivery must not unlock a component or repeat completion side effects.

Browser storage is origin/profile-scoped and user-controlled. Clearing it loses unsent local drafts and creates a new conversation unless its old identity is retained. It does not erase server history. **Clear all messages** hides IDs locally, without deleting server files or cancelling pending requests. Storage quota/restrictions can prevent durable saving; the host sends no new component identity when its initial save fails.

## Operational limits

One process exclusively owns a storage folder; do not run two instances against it. Accepted IDs and terminal records are intentionally retained even though the old retention option exists. Durable queues/history have no automatic pruning or database compaction. Back up the whole storage folder with the engine stopped, protect permissions, and monitor disk growth.

Unfinished adapter execution is at least once. An external operation can succeed before a crash and then repeat before the local checkpoint commits; the adapter must deduplicate externally by immutable input ID. Tests demonstrate no repeated adapter invocation after a successful durable outcome commit, not an exactly-once transaction with an external service.

Keep approved V1/Phase 2/3/4 state readable; no state-version migration is introduced by this documentation release. Generic limits apply to component payloads; invalid excessive persisted component data fails closed rather than being silently repaired. Do not edit or replace a corrupt snapshot with empty state to make startup succeed.
