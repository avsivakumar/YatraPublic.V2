import { componentStorageKey, fingerprint, isTerminal, readSubmission, reconcileComponentState, restoreComponentState, type ComponentState } from '../componentState';
import { saveState } from '../persistence';
import { Component, useEffect, useRef, useState, type ReactNode } from 'react';
import { isComponentRequest, isComponentResponse, isObject, type ComponentLifecycle, type ComponentRequestPayload, type ComponentResponsePayload } from '../componentProtocol';
import { componentRegistry, type ComponentRegistry, type ComponentRegistration } from '../componentRegistry';
import { submitMessage, SubmissionError } from '../api';
import { newUlid } from '../ulid';
import type { BrowserMessageSubmission, YatraMessage } from '../types';

function Unavailable() {
  return <article className="unsupported-message" role="status">Component unavailable. This interaction cannot be displayed.</article>;
}
class RenderBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  render() { return this.state.failed ? <Unavailable /> : this.props.children; }
}
interface HostProps { payload: unknown; registry?: ComponentRegistry; message?: YatraMessage; messages?: YatraMessage[]; replaying?: boolean }
export function ComponentHost({ payload, registry = componentRegistry, message, messages = [], replaying = false }: HostProps) {
  if (!isComponentRequest(payload)) return <Unavailable />;
  const entry = registry.resolve(payload.componentId, payload.componentVersion);
  try {
    if (!entry || !entry.validateProps(payload.props).valid || !payload.allowedActions.every(action => entry.supportedActions.includes(action))) return <Unavailable />;
  } catch { return <Unavailable />; }
  const View = entry.render;
  // Payload-only usage remains a non-interactive preview, preserving the Phase 1 boundary.
  return <RenderBoundary key={`${message?.messageId ?? ''}:${JSON.stringify(payload)}`}>
    {message ? <InteractiveHost request={payload} message={message} messages={messages} entry={entry} replaying={replaying} /> :
      <View instanceId={payload.instanceId} title={payload.title} description={payload.description} props={payload.props} allowedActions={[]} lifecycle="unsupported" emitAction={() => undefined} />}
  </RenderBoundary>;
}


function InteractiveHost({ request, message, messages, entry, replaying }: { request: ComponentRequestPayload; message: YatraMessage; messages: YatraMessage[]; entry: ComponentRegistration; replaying: boolean }) {
  const [saved, setSaved] = useState(() => restoreComponentState(message, request, entry));
  const [validationError, setValidationError] = useState('');
  const errorRef = useRef<HTMLDivElement>(null);
  const locked = useRef(false);
  const mounted = useRef(true);
  const currentMessages = useRef(messages);
  currentMessages.current = messages;
  const effective = reconcileComponentState(saved, message, request, entry, messages);
  const latest = useRef(effective);
  latest.current = effective;
  const lifecycle = effective.lifecycle;
  const response = readSubmission(effective)?.payload;
  const storageKey = componentStorageKey(message);
  const View = entry.render;

  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  useEffect(() => {
    if (effective !== saved) {
      saveState(storageKey, effective);
      setSaved(effective);
    }
  }, [effective, saved, storageKey]);

  function showError(text: string) {
    if (!mounted.current) return;
    setValidationError(text);
    queueMicrotask(() => errorRef.current?.focus());
  }
  function persist(next: ComponentState): boolean {
    if (!saveState(storageKey, next)) {
      showError('The interaction could not be saved. No new response will be sent until browser storage is available.');
      return false;
    }
    latest.current = next;
    if (mounted.current) setSaved(next);
    return true;
  }
  async function sendDraft() {
    const submission = readSubmission(latest.current);
    if (!submission || isTerminal(latest.current.lifecycle)) { locked.current = false; return; }
    if (!persist({ ...latest.current, lifecycle: 'submitting' })) { locked.current = false; return; }
    try {
      await submitMessage(message.conversationId, submission);
      finish('submitted', true);
    } catch (error) {
      finish(error instanceof SubmissionError && !error.retryable ? 'failed' : 'retryable', false);
    } finally { locked.current = false; }
  }
  function finish(state: ComponentLifecycle, acknowledged: boolean) {
    // A remounted host or an early SSE outcome may have advanced the durable record.
    const restored = restoreComponentState(message, request, entry);
    const durable = reconcileComponentState(restored, message, request, entry, currentMessages.current);
    const current = isTerminal(latest.current.lifecycle) ? latest.current : durable;
    if (isTerminal(current.lifecycle)) { persist({ ...current, acknowledged: current.acknowledged || acknowledged }); return; }
    if (current.acknowledged && !acknowledged) return;
    persist({ ...current, lifecycle: state, acknowledged: current.acknowledged || acknowledged });
  }
  function emitAction(action: string, data: unknown) {
    if (locked.current || replaying || lifecycle !== 'ready' || latest.current.submission || !message.expectsReply) return;
    locked.current = true;
    try {
      if (!request.allowedActions.includes(action) || !entry.supportedActions.includes(action) || !entry.validateResponse(action, data, request.props).valid || !isObject(data)) {
        locked.current = false;
        showError('The action is invalid. Please review your selection.');
        return;
      }
      const candidate = { componentId: request.componentId, componentVersion: request.componentVersion, instanceId: request.instanceId, action, data };
      if (!isComponentResponse(candidate)) throw new Error('Invalid or excessive component response');
      const payload: ComponentResponsePayload = JSON.parse(JSON.stringify(candidate));
      const submission: BrowserMessageSubmission<ComponentResponsePayload> = { messageId: newUlid(), type: 'component.response', inReplyTo: message.messageId, payload };
      const next: ComponentState = { version: 1, request: fingerprint(request), lifecycle: 'submitting', acknowledged: false, submission: JSON.stringify(submission) };
      // The immutable identity and exact JSON are durable before the first HTTP attempt.
      if (!persist(next)) { locked.current = false; return; }
      setValidationError('');
      void sendDraft();
    } catch {
      locked.current = false;
      showError('The action is invalid. Please review your selection.');
    }
  }
  const status = {
    ready: 'Ready for your choice.', submitting: 'Submitting your response…', retryable: 'Submission could not be confirmed. Retry the same response.',
    submitted: 'Response accepted. Waiting for confirmation.', completed: 'Interaction completed.', cancelled: 'Interaction cancelled.',
    failed: 'This interaction could not be completed.', expired: 'This interaction has expired.', unsupported: 'Component unavailable.',
  }[lifecycle];
  return <div>
    <View instanceId={request.instanceId} title={request.title} description={request.description} props={request.props} allowedActions={request.allowedActions}
      lifecycle={!message.expectsReply ? 'unsupported' : replaying && !isTerminal(lifecycle) ? 'submitting' : lifecycle} outcome={{ status: lifecycle, action: response?.action, data: response?.data }} emitAction={emitAction} />
    <div className="component-status" role="status" aria-live="polite" aria-atomic="true">{replaying && !isTerminal(lifecycle) ? 'Restoring interaction…' : status}</div>
    {validationError && <div role="alert" tabIndex={-1} ref={errorRef}>{validationError}</div>}
    {lifecycle === 'retryable' && <button type="button" disabled={replaying} className="component-retry" onClick={() => { if (!locked.current && !replaying && !isTerminal(latest.current.lifecycle)) { locked.current = true; void sendDraft(); } }}>Retry response</button>}
  </div>;
}
