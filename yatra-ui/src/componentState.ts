import { isComponentResponse, isObject, type ComponentLifecycle, type ComponentRequestPayload } from './componentProtocol';
import type { ComponentRegistration } from './componentRegistry';
import type { BrowserMessageSubmission, ComponentResponsePayload, YatraMessage } from './types';
import { readStoredState } from './persistence';

export interface ComponentState {
  version: 1;
  request: string;
  lifecycle: ComponentLifecycle;
  acknowledged: boolean;
  // This serialized submission is authoritative and is never rebuilt on retry.
  submission?: string;
  outcomeMessageId?: string;
}
export const isTerminal = (state: ComponentLifecycle) => ['completed', 'cancelled', 'expired', 'failed', 'unsupported'].includes(state);
export const componentStorageKey = (message: YatraMessage) => `component:${message.conversationId}:${message.messageId}`;
export function fingerprint(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(fingerprint).join(',')}]`;
  if (isObject(value)) return `{${Object.keys(value).sort().map(key => `${JSON.stringify(key)}:${fingerprint(value[key])}`).join(',')}}`;
  return JSON.stringify(value);
}
export function readSubmission(state: ComponentState): BrowserMessageSubmission<ComponentResponsePayload> | undefined {
  return state.submission ? JSON.parse(state.submission) : undefined;
}
export function validResponse(value: unknown, request: ComponentRequestPayload, entry: ComponentRegistration): value is ComponentResponsePayload {
  return isComponentResponse(value) && value.componentId === request.componentId && value.componentVersion === request.componentVersion
    && value.instanceId === request.instanceId && request.allowedActions.includes(value.action) && entry.validateResponse(value.action, value.data, request.props).valid;
}
export function restoreComponentState(message: YatraMessage, request: ComponentRequestPayload, entry: ComponentRegistration): ComponentState {
  const initial: ComponentState = { version: 1, request: fingerprint(request), lifecycle: 'ready', acknowledged: false };
  const fail = () => ({ ...initial, lifecycle: 'failed' as const });
  const stored = readStoredState(componentStorageKey(message));
  if (stored.kind === 'missing') return initial;
  if (stored.kind === 'invalid') return fail();
  try {
    const value = stored.value;
    if (!isObject(value) || value.version !== 1 || value.request !== initial.request || typeof value.acknowledged !== 'boolean'
      || typeof value.lifecycle !== 'string' || !['ready', 'submitting', 'retryable', 'submitted', 'completed', 'cancelled', 'expired', 'failed', 'unsupported'].includes(value.lifecycle)) return fail();
    if (value.outcomeMessageId !== undefined && typeof value.outcomeMessageId !== 'string') return fail();
    if (value.submission !== undefined) {
      if (typeof value.submission !== 'string') return fail();
      const submission: unknown = JSON.parse(value.submission);
      if (!isObject(submission) || Object.keys(submission).some(key => !['messageId', 'type', 'inReplyTo', 'payload'].includes(key))
        || submission.type !== 'component.response' || submission.inReplyTo !== message.messageId
        || typeof submission.messageId !== 'string' || submission.messageId.length !== 26 || !/^[0-7][0-9A-HJKMNP-TV-Z]{25}$/i.test(submission.messageId)
        || !validResponse(submission.payload, request, entry)) return fail();
      if (value.lifecycle === 'cancelled' && submission.payload.action !== 'cancel') return fail();
      if (value.lifecycle === 'completed' && submission.payload.action === 'cancel') return fail();
    } else if (['submitting', 'retryable', 'submitted', 'completed', 'cancelled'].includes(value.lifecycle) || value.acknowledged) return fail();
    if (value.lifecycle === 'ready' && (value.submission !== undefined || value.acknowledged)) return fail();
    if (value.lifecycle === 'submitted' && !value.acknowledged) return fail();
    if (['submitting', 'retryable'].includes(value.lifecycle) && value.acknowledged) return fail();
    const restored = value as unknown as ComponentState;
    return { ...restored, lifecycle: restored.lifecycle === 'submitting' ? 'retryable' : restored.lifecycle };
  } catch { return fail(); }
}

export function reconcileComponentState(current: ComponentState, message: YatraMessage, request: ComponentRequestPayload, entry: ComponentRegistration, messages: YatraMessage[]): ComponentState {
  if (isTerminal(current.lifecycle)) return current;
  let next = current;
  let submission = readSubmission(next);
  const response = messages.find(candidate => candidate.type === 'component.response' && candidate.inReplyTo === message.messageId
    && (!submission || candidate.messageId === submission.messageId) && validResponse(candidate.payload, request, entry));
  if (response) {
    submission ??= { messageId: response.messageId, type: 'component.response', inReplyTo: message.messageId, payload: response.payload as ComponentResponsePayload };
    next = { ...next, submission: next.submission ?? JSON.stringify(submission), lifecycle: 'submitted', acknowledged: true };
  }
  for (const outcome of messages) {
    const requestOutcome = outcome.inReplyTo === message.messageId;
    const responseOutcome = submission && outcome.inReplyTo === submission.messageId;
    if (outcome.type === 'agent.text' && responseOutcome) {
      return { ...next, lifecycle: submission!.payload.action === 'cancel' ? 'cancelled' : 'completed', outcomeMessageId: outcome.messageId };
    }
    if (outcome.type === 'status' && requestOutcome && isObject(outcome.payload) && outcome.payload.componentLifecycle === 'expired')
      return { ...next, lifecycle: 'expired', outcomeMessageId: outcome.messageId };
    if (outcome.type === 'error' && (requestOutcome || responseOutcome))
      return { ...next, lifecycle: isObject(outcome.payload) && outcome.payload.code === 'pending_reply_expired' ? 'expired' : 'failed', outcomeMessageId: outcome.messageId };
  }
  return fingerprint(next) === fingerprint(current) ? current : next;
}
