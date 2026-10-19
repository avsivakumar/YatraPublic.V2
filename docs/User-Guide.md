# User guide — Yatra V2

Yatra demonstrates conversation text, dynamic forms and trusted inline component interactions. The default Fake LLM runs without credentials. It is a local architectural sample, with no account, permission or production workflow features.

## Start

Use the [README quick start](../README.md#run-the-sample). Run the backend HTTPS profile for the existing Vite proxy, or use its explicit HTTP-only alternative. Open `http://localhost:5173` and wait for **connected**.

The header shows the conversation identifier and connection status; the main area holds message/form/component cards; the composer sends text. The current conversation is stored for the browser profile/origin. Keep both server terminals open.

## Customer comparison

Enter `compare customers`. Inspect ABC Industries and XYZ Systems, optionally expand their details, select a radio option, then choose **Confirm selection**. Keyboard users can Tab to the radio group, use Space/arrow keys to select and activate the confirmation button with Enter. **Cancel comparison** sends cancellation instead.

Actions disable immediately. **Response accepted. Waiting for confirmation.** means the engine accepted the response; it does not yet mean processing succeeded. A correlated confirmation changes the status to **Interaction completed.** or **Interaction cancelled.** The comparison stays visible and read-only, with the submitted customer marked selected.

## Status summary

Enter `show status summary`. Read the status/summary and choose **Acknowledge status**. It uses the same submission/recovery path and locks after acknowledgement/completion. This illustrative status performs no external business action.

The browser advertises its compiled component versions automatically. An unavailable or incompatible capability produces plain fallback text. Unknown versions and malformed/over-limit requests show a non-interactive unavailable card; they do not load remote UI.

## Existing forms and text

- `show contact form`: enter the requested contact information.
- `request application access`: complete the access-request example.
- `confirm an action`: use the confirmation form.
- `run sample skill`: enter its inputs and receive a processed sample result.
- Other text returns the configured model's response (Fake by default).

V1 forms retain their field rules, inline validation, draft restoration, submit/cancel controls and explicit-rejection correction behavior. The protocol documents the exact date/time, numeric and selection value formats. Hidden/read-only values are not proof of identity or permission.

Commands ignore surrounding whitespace and case. Multiple forms and components can be pending at once; answer the intended card, regardless of display order.

## Recovery and limits

A failed/uncertain component transport shows **Retry response**. Retry sends the original saved action and identity, rather than creating another choice. Reload during submission restores that uncertainty safely. Reload after acceptance keeps actions locked while awaiting confirmation. Completed/cancelled/failed/expired interactions cannot be answered again; a new decision needs a new request.

The UI reconnects SSE automatically with bounded delays, resumes history and deduplicates messages. If attempts exhaust, use **Retry connection**. Text requires a connected stream. Existing form/component HTTP submission may still work while delivery reconnects; hosted actions lock temporarily during replay reconstruction.

The UI displays the 20 most recent component requests as hosted views. Older component requests show a limit notice without silently cancelling their server pending state. Forms/text are not subject to that component-view cap.

Browser storage is scoped to origin/profile; changing the port or clearing site data can hide unsent drafts or use another conversation. Server state survives browser reloads. **Clear all messages** only hides local history; it does not erase server data or cancel requests. Do not enter private data into an unprotected sample deployment.

For storage errors, a persistent submitted state, unsupported content or startup failure, use [Troubleshooting](Troubleshooting.md). The [V1 user guide](v1/docs/User-Guide.md) remains available as a historical record of the original release.
