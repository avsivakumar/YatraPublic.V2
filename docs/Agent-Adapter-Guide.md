# Agent adapter guide — Yatra V2

An adapter implements `IOrchestrationAgentAdapter.HandleAsync(AgentInput, IAgentOutputSink, CancellationToken)`. The engine depends on this neutral interface, not concrete domain or model code. Publish outputs through the supplied sink; do not call the browser or mutate engine storage directly.

## Input and publication

`AgentInput.Message` is the trusted envelope. `ResolvedReturnPath` is supplied for a claimed reply. For component responses, `ComponentRequest` is the original server-side request context. Verify the resolved path belongs to your requester before accepting a response.

Use fresh message ULIDs, UTC time, the conversation ID, a known message type and `inReplyTo` correlation. An actionable form/component request has `ExpectsReply = true` and `new AgentOutput(request, new ReturnPath("orchestrator/sample"))`. The return path remains private; it is not a payload property or browser input. Text outcomes reference the triggering input's message ULID, so component confirmation references the response, not the original request.

## Discovery and exact contracts

New user-text payloads may contain `componentCapabilities`. Parse them with `ComponentCapabilities.TryRead`: exact canonical ID/version/actions only. No schemas or executable URLs are supported. The sample catalog publishes only known domain contracts and requires all requested actions. Explicit missing/malformed support produces safe correlated text without pending work. Absence of the whole field retains the original legacy customer-comparison contract only; new status-summary needs explicit capability support.

Capabilities are advisory. Neither a claimed capability nor browser-repeated request data is authorization or business truth. The UI still performs exact registry lookup; the engine and authoritative validator still bind the original request and response.

## Authoritative validation

Forms retain `IFormResponseValidator`, their original field/version rules and explicit-rejection replacement semantics.

Components use `IComponentResponseValidator`: exact `ValidatorKey`, `IsValidAsync(JsonElement response, PendingReply pendingReply, CancellationToken)`. Reject unknown IDs/versions, wrong instance, disallowed action, extra fields, invalid data and business IDs absent from the original properties. The engine requires exactly one matching validator and checks generic pending correlation before dispatch. The sample domain catalog validates again before creating an outcome. See [component authoring](Component-Authoring.md).

## Outcomes, cancellation and retry

An HTTP 202 means durable queue acceptance, not successful business processing. A component select/acknowledge outcome completes the original interaction; cancel confirms cancellation. A correlated error is terminal for the component host. Do not use a retryable error to silently edit its saved response or unlock a terminal interaction; issue a new request identity when a new human decision is needed.

Let caller `OperationCanceledException` propagate. Unfinished durable component work retries on requester exceptions using the same input identity. Component outputs are staged until successful adapter return, and commit atomically with completion/terminal state. An explicit error outcome commits failure. V1 forms retain their existing retryable rejection behavior; do not conflate the paths.

An external effect can succeed before a process crashes and before the local checkpoint commits. Deduplicate externally by `AgentInput.Message.MessageId`; at-least-once execution is not an exactly-once external transaction. The built-in customer/status outcomes perform no external business effect.

## Composition and model boundary

`AddSampleYatraOrchestration()` registers the sample adapter and the form/customer/status validators. A replacement host supplies one adapter and its validators alongside `AddYatraEngine(configuration)`. Keep concrete registration outside Engine.

The Fake LLM is the distributable default. The optional OpenAI sample uses private constants and a placeholder key; tests inject `ILlmClient` and never call a paid service. Do not share populated constants or compiled keys. A production integration must provide its own secure secret configuration and access controls.
