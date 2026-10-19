# Yatra V2 use cases

The sample demonstrates an adapter asking a person for structured input and continuing the same correlated conversation.

| Demonstration | Implemented interaction |
| --- | --- |
| Compare candidate customer records | Trusted customer cards; select one original ID or cancel |
| Acknowledge a status | Trusted plain-text status summary; acknowledge with empty data |
| Collect structured values | V1 forms with approved field types and authoritative validation |
| Continue after user input | Original private requester path and response/outcome ULID correlation |
| Recover after interruption | Same-identity retry, persisted pending/terminal state and SSE replay |

A developer can adapt these patterns to a real domain only by supplying reviewed code, strict validators, external idempotency and the missing production controls. The illustrative status does not operate an external system, and the access-request form is not an authorization service. No payment, signing, file-upload, arbitrary agent UI, marketplace or enterprise workflow engine is implemented.

See [scope](Yatra-Version-2-Scope.md), [security](Security.md), and the [historical V1 examples](v1/docs/Yatra-Use-Cases.md).
