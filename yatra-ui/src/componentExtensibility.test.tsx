import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import vectors from '../../tests/fixtures/component-limits.json';
import { componentLimits, withinComponentLimits } from './componentLimits';
import { isComponentCapabilities } from './componentCapabilities';
import { ComponentRegistry, componentRegistry } from './componentRegistry';
import { ComponentHost } from './components/ComponentHost';
import { MessageList } from './components/MessageList';
import { validStatusSummaryProps, validStatusSummaryResponse } from './components/statusSummary/validation';
import { App } from './App';
import type { ComponentRequestPayload, YatraMessage } from './types';

const request: ComponentRequestPayload = { componentId: 'status-summary', componentVersion: '1.0', instanceId: '01K00000000000000000000001', title: 'Status card', props: { status: 'Ready', summary: 'Awaiting acknowledgement.' }, allowedActions: ['acknowledge'] };
const message: YatraMessage = { messageId: '01K00000000000000000000002', conversationId: '01K00000000000000000000000', type: 'component.request', source: 'sample', destination: 'ui', createdAtUtc: '', expectsReply: true, payload: request };
function build(kind: string, count: number): unknown {
  if (kind === 'array') return Array.from({ length: count }, (_, index) => index);
  if (kind === 'properties') return Object.fromEntries(Array.from({ length: count }, (_, index) => [`p${index}`, 0]));
  if (kind === 'key') return { ['x'.repeat(count)]: 0 };
  if (kind === 'string') return 'x'.repeat(count);
  if (kind === 'depth') { let value: unknown = 'leaf'; for (let index = 0; index < count; index++) value = { child: value }; return value; }
  if (kind === 'nodes') return Array.from({ length: 21 }, (_, index) => Array(index < 20 ? 100 : count - 2022).fill(0));
  let remaining = count - 52;
  return Array.from({ length: 17 }, () => { const size = Math.min(remaining, 4096); remaining -= size; return kind === 'unicode' ? 'é'.repeat(Math.floor(size / 2)) + (size % 2 ? 'x' : '') : 'x'.repeat(size); });
}
class Stream extends EventTarget {
  static instances: Stream[] = [];
  onopen: (() => void) | null = null; onerror: ((event: Event) => void) | null = null;
  constructor(public url: string) { super(); Stream.instances.push(this); }
  close() {}
  emit(value: YatraMessage) { this.dispatchEvent(new MessageEvent(value.type, { data: JSON.stringify(value) })); }
}
beforeEach(() => { localStorage.clear(); Stream.instances = []; vi.stubGlobal('EventSource', Stream); vi.stubGlobal('fetch', vi.fn(async (_url, init) => new Response(JSON.stringify({ messageId: JSON.parse(init.body).messageId, status: 'accepted', replayed: false }), { status: 202 }))); });
afterEach(() => vi.unstubAllGlobals());

describe('Phase 4 payload budgets', () => {
  it.each(vectors)('$name', ({ kind, count, valid }) => { const value = build(kind, count); if (kind === 'bytes' || kind === 'unicode') expect(new TextEncoder().encode(JSON.stringify(value)).length).toBe(count); expect(withinComponentLimits(value)).toBe(valid); });
  it('rejects cycles and non-JSON values without throwing', () => { const value: Record<string, unknown> = {}; value.loop = value; expect(withinComponentLimits(value)).toBe(false); expect(withinComponentLimits({ value: () => undefined })).toBe(false); expect(withinComponentLimits({ value: NaN })).toBe(false); });
  it('never renders or submits excessive properties', () => { render(<ComponentHost payload={{ ...request, props: { status: 'Ready', summary: 'x'.repeat(100_000) } }} message={message} />); expect(screen.getByRole('status')).toHaveTextContent('Component unavailable'); expect(screen.queryByRole('button')).not.toBeInTheDocument(); expect(fetch).not.toHaveBeenCalled(); });
  it('checks generic response limits before persisting or sending', async () => {
    const registry = new ComponentRegistry(); registry.register({ componentId: 'test-large', componentVersion: '1.0', supportedActions: ['acknowledge'], validateProps: () => ({ valid: true }), validateResponse: () => ({ valid: true }), render: ({ emitAction }) => <button onClick={() => emitAction('acknowledge', { value: 'x'.repeat(4097) })}>Emit excessive data</button> });
    render(<ComponentHost payload={{ ...request, componentId: 'test-large' }} message={message} registry={registry} />); fireEvent.click(screen.getByRole('button')); expect(await screen.findByRole('alert')).toHaveTextContent('invalid'); expect(fetch).not.toHaveBeenCalled();
  });
  it('caps hosted views while preserving V1 text/forms and newer interactions', () => {
    const messages = Array.from({ length: 23 }, (_, index) => ({ ...message, messageId: String(index).padStart(26, '0'), payload: { ...request, title: `Status ${index}` } }));
    render(<MessageList messages={[...messages, { ...message, messageId: 'text', type: 'agent.text', payload: { text: 'V1 text remains' } }, { ...message, messageId: 'form', type: 'form.request' }]} renderForm={() => <div>V1 form remains</div>} />);
    expect(screen.getAllByRole('button', { name: 'Acknowledge status' })).toHaveLength(componentLimits.maxHostedComponents);
    expect(screen.queryByRole('heading', { name: 'Status 0' })).not.toBeInTheDocument(); expect(screen.getByRole('heading', { name: 'Status 22' })).toBeInTheDocument();
    expect(screen.getAllByText(/Component rendering limit reached/)).toHaveLength(3); expect(screen.getByText('V1 text remains')).toBeInTheDocument(); expect(screen.getByText('V1 form remains')).toBeInTheDocument();
  });
});

describe('capability discovery and exact versions', () => {
  it('describes only the exact registered IDs, versions and actions', () => {
    expect(componentRegistry.getCapabilities()).toEqual([
      { componentId: 'customer-comparison', componentVersion: '1.0', supportedActions: ['select', 'cancel'] },
      { componentId: 'status-summary', componentVersion: '1.0', supportedActions: ['acknowledge'] },
    ]);
    expect(isComponentCapabilities(componentRegistry.getCapabilities())).toBe(true);
    expect(Object.isFrozen(componentRegistry.getCapabilities())).toBe(true); expect(Object.isFrozen(componentRegistry.getCapabilities()[0].supportedActions)).toBe(true);
  });
  it('allows exact version coexistence without fallback or registration mutation', () => {
    const registry = new ComponentRegistry(); const original = componentRegistry.resolve('status-summary', '1.0')!;
    registry.register(original); registry.register({ ...original, componentVersion: '1.1' });
    expect(registry.resolve('status-summary', '1.0')!.componentVersion).toBe('1.0'); expect(registry.resolve('status-summary', '1.1')!.componentVersion).toBe('1.1'); expect(registry.resolve('status-summary', '1.2')).toBeUndefined(); expect(registry.resolve('Status-summary', '1.0')).toBeUndefined();
    expect(() => registry.register(original)).toThrow('Duplicate'); expect(registry.getCapabilities()).toHaveLength(2);
  });
  it('rejects invalid, duplicate and excessive capability lists/registrations', () => {
    const original = componentRegistry.getCapabilities()[1];
    for (const value of [null, {}, [{ ...original, componentVersion: 'latest' }], [{ ...original, componentId: 'Status-Summary' }], [{ ...original, supportedActions: [] }], [{ ...original, supportedActions: ['acknowledge', 'acknowledge'] }], [{ ...original, source: 'https://invalid.example/module.js' }], [original, original], Array.from({ length: 33 }, (_, i) => ({ ...original, componentId: `card-${i}` }))]) expect(isComponentCapabilities(value)).toBe(false);
    expect(isComponentCapabilities([])).toBe(true);
    const registry = new ComponentRegistry(); expect(() => registry.register({ ...componentRegistry.resolve('status-summary', '1.0')!, componentVersion: 'latest' })).toThrow('Invalid');
    for (let index = 0; index < 32; index++) registry.register({ ...componentRegistry.resolve('status-summary', '1.0')!, componentId: `card-${index}` });
    expect(() => registry.register({ ...componentRegistry.resolve('status-summary', '1.0')!, componentId: 'too-many' })).toThrow('limit');
  });
  it('advertises the current registry with user text through the existing POST', async () => {
    render(<App />); act(() => Stream.instances[0].onopen?.()); fireEvent.change(screen.getByRole('textbox', { name: 'Message' }), { target: { value: 'show status summary' } }); fireEvent.click(screen.getByRole('button', { name: 'Send message' }));
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1)); const submitted = JSON.parse(vi.mocked(fetch).mock.calls[0][1]!.body as string);
    expect(submitted.payload.componentCapabilities).toEqual(componentRegistry.getCapabilities()); expect(submitted.payload.text).toBe('show status summary'); expect(submitted.type).toBe('user.text');
  });
});

describe('status summary extension', () => {
  it('uses the generic timeline host, submits acknowledge, completes and restores read-only', async () => {
    const view = render(<MessageList messages={[message]} renderForm={() => null} />); expect(screen.getByRole('heading', { name: 'Status card' })).toBeInTheDocument(); expect(screen.getByText('Awaiting acknowledgement.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Acknowledge status' })); await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
    const submitted = JSON.parse(vi.mocked(fetch).mock.calls[0][1]!.body as string); expect(submitted).toMatchObject({ type: 'component.response', inReplyTo: message.messageId, payload: { componentId: 'status-summary', componentVersion: '1.0', instanceId: request.instanceId, action: 'acknowledge', data: {} } });
    const outcome = { ...message, messageId: 'terminal', type: 'agent.text', inReplyTo: submitted.messageId, payload: { text: 'Status summary was acknowledged.' } };
    view.rerender(<MessageList messages={[message, outcome]} renderForm={() => null} />); expect(await screen.findByText('Interaction completed.')).toBeInTheDocument();
    view.unmount(); render(<ComponentHost payload={request} message={message} />); expect(screen.getByRole('button', { name: 'Acknowledge status' })).toBeDisabled(); expect(screen.getByRole('status')).toHaveTextContent('Interaction completed.');
  });
  it('requires the exact supported version and escapes plain-text content', () => {
    const view = render(<ComponentHost payload={{ ...request, componentVersion: '1.1' }} message={message} />); expect(screen.getByRole('status')).toHaveTextContent('Component unavailable');
    view.rerender(<ComponentHost payload={{ ...request, props: { status: '<b>Ready</b>', summary: '<script>bad()</script>' } }} message={message} />);
    expect(screen.getByText('<b>Ready</b>')).toBeInTheDocument(); expect(screen.getByText('<script>bad()</script>')).toBeInTheDocument(); expect(document.querySelector('script')).toBeNull();
  });
  it.each([{ status: '', summary: 'ok' }, { status: 'ok' }, { status: 'ok', summary: 'x'.repeat(1001) }, { status: 'x'.repeat(101), summary: 'ok' }, { status: 'ok', summary: 'ok', extra: true }, { status: 'ok', summary: null }])('rejects invalid status properties %j', props => expect(validStatusSummaryProps(props)).toBe(false));
  it('validates both text boundaries and strict empty acknowledge data', () => {
    const props = { status: 'x'.repeat(100), summary: 'x'.repeat(1000) }; expect(validStatusSummaryProps(props)).toBe(true); expect(validStatusSummaryResponse('acknowledge', {}, props)).toBe(true); expect(validStatusSummaryResponse('cancel', {}, props)).toBe(false); expect(validStatusSummaryResponse('acknowledge', { extra: true }, props)).toBe(false);
  });
});

