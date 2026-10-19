import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import cases from '../../tests/fixtures/customer-comparison-props.json';
import { validCustomerProps, validCustomerResponse } from './components/customerComparison/validation';
import { ComponentHost } from './components/ComponentHost';
import { ComponentRegistry } from './componentRegistry';
import { App } from './App';
import type { ComponentRequestPayload, YatraMessage } from './types';
const props = { customers: [{ id: 'C101', name: 'ABC Industries', status: 'Active', location: 'Dallas' }, { id: 'C205', name: 'XYZ Systems', status: 'Pending' }] };
const payload: ComponentRequestPayload = { componentId: 'customer-comparison', componentVersion: '1.0', instanceId: '01K00000000000000000000001', title: 'Compare', props, allowedActions: ['select', 'cancel'] };
const message: YatraMessage = { messageId: '01K00000000000000000000002', conversationId: '01K00000000000000000000000', type: 'component.request', source: 'sample', destination: 'ui', createdAtUtc: '', expectsReply: true, payload };
class Stream extends EventTarget {
  static instances: Stream[] = [];
  onopen: (() => void) | null = null;
  onerror: ((event: Event) => void) | null = null;
  constructor(public url: string) { super(); Stream.instances.push(this); }
  close() {}
  emit(value: YatraMessage) { this.dispatchEvent(new MessageEvent(value.type, { data: JSON.stringify(value) })); }
}
beforeEach(() => {
  localStorage.clear(); sessionStorage.clear(); Stream.instances = [];
  vi.stubGlobal('EventSource', Stream);
  vi.stubGlobal('fetch', vi.fn(async (_url, init) => new Response(JSON.stringify({ messageId: JSON.parse(init.body).messageId, status: 'accepted', replayed: false }), { status: 202 })));
});
afterEach(() => vi.unstubAllGlobals());
describe('customer comparison properties', () => {
  it.each(cases)('$name', test => expect(validCustomerProps(test.props)).toBe(test.valid));
  it.each([['select', { customerId: 'C101' }, true], ['select', { customerId: 'other' }, false], ['select', { customerId: 'C101', extra: true }, false], ['cancel', {}, true], ['cancel', { customerId: 'C101' }, false], ['delete', {}, false]])('validates %s %j', (action, data, valid) => expect(validCustomerResponse(action as string, data, props)).toBe(valid));
});
describe('customer comparison interaction', () => {
  it('supports keyboard selection, emits authoritative correlation, and locks after acknowledgement', async () => {
    const user = userEvent.setup(); render(<ComponentHost payload={payload} message={message} />);
    expect(screen.getAllByRole('radio')).toHaveLength(2);
    await user.tab(); expect(screen.getByRole('radio', { name: /ABC/ })).toHaveFocus();
    await user.keyboard(' '); await user.tab(); await user.keyboard('{Enter}');
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
    const submitted = JSON.parse(vi.mocked(fetch).mock.calls[0][1]!.body as string);
    expect(submitted).toMatchObject({ type: 'component.response', inReplyTo: message.messageId, payload: { componentId: payload.componentId, componentVersion: '1.0', instanceId: payload.instanceId, action: 'select', data: { customerId: 'C101' } } });
    expect(Object.keys(submitted).sort()).toEqual(['inReplyTo', 'messageId', 'payload', 'type']);
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Response accepted'));
    expect(screen.getByRole('button', { name: 'Confirm selection' })).toBeDisabled();
  });
  it('submits cancel and retains read-only comparison on correlated confirmation', async () => {
    const user = userEvent.setup(); const view = render(<ComponentHost payload={payload} message={message} />);
    await user.click(screen.getByRole('button', { name: 'Cancel comparison' }));
    const sent = JSON.parse(vi.mocked(fetch).mock.calls[0][1]!.body as string);
    expect(sent.payload).toMatchObject({ action: 'cancel', data: {} });
    view.rerender(<ComponentHost payload={payload} message={message} messages={[{ ...message, messageId: 'outcome', type: 'agent.text', inReplyTo: sent.messageId, payload: { text: 'Cancelled' } }]} />);
    expect(screen.getByRole('status')).toHaveTextContent('Interaction cancelled'); expect(screen.getByText('ABC Industries')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancel comparison' })).toBeDisabled();
  });
  it('renders only allowed actions', () => {
    render(<ComponentHost payload={{ ...payload, allowedActions: ['cancel'] }} message={message} />);
    expect(screen.queryByRole('radio')).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Confirm selection' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancel comparison' })).toBeEnabled();
  });
  it('guards synchronous double actions before rerender', async () => {
    const registry = new ComponentRegistry();
    registry.register({ componentId: 'test-double', componentVersion: '1.0', supportedActions: ['select'], validateProps: () => ({ valid: true }), validateResponse: () => ({ valid: true }), render: ({ emitAction }) => <button onClick={() => { emitAction('select', {}); emitAction('select', {}); }}>Double</button> });
    render(<ComponentHost payload={{ ...payload, componentId: 'test-double', allowedActions: ['select'] }} registry={registry} message={message} />);
    fireEvent.click(screen.getByRole('button', { name: 'Double' })); await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
  });
  it('blocks invalid component actions and moves focus to validation feedback', async () => {
    const registry = new ComponentRegistry();
    registry.register({ componentId: 'test-invalid', componentVersion: '1.0', supportedActions: ['select'], validateProps: () => ({ valid: true }), validateResponse: () => ({ valid: false }), render: ({ emitAction }) => <button onClick={() => emitAction('cancel', {})}>Invalid action</button> });
    render(<ComponentHost payload={{ ...payload, componentId: 'test-invalid', allowedActions: ['select'] }} registry={registry} message={message} />);
    fireEvent.click(screen.getByRole('button')); await waitFor(() => expect(screen.getByRole('alert')).toHaveFocus()); expect(fetch).not.toHaveBeenCalled();
  });
  it('keeps a confirmed selection visible', async () => {
    const user = userEvent.setup(); const view = render(<ComponentHost payload={payload} message={message} />);
    await user.click(screen.getByRole('radio', { name: /XYZ/ })); await user.click(screen.getByRole('button', { name: 'Confirm selection' }));
    const sent = JSON.parse(vi.mocked(fetch).mock.calls[0][1]!.body as string);
    view.rerender(<ComponentHost payload={payload} message={message} messages={[{ ...message, type: 'agent.text', messageId: 'outcome', inReplyTo: sent.messageId, payload: { text: 'Selected' } }]} />);
    expect(screen.getByRole('status')).toHaveTextContent('Interaction completed'); expect(screen.getByRole('radio', { name: /XYZ/ })).toBeChecked(); expect(screen.getByRole('radio', { name: /XYZ/ })).toBeDisabled();
  });
  it('runs command → SSE component → action HTTP → SSE confirmation in App', async () => {
    const user = userEvent.setup(); render(<App />); const stream = Stream.instances[0]; act(() => stream.onopen?.());
    await user.type(screen.getByRole('textbox', { name: 'Message' }), 'compare customers'); await user.click(screen.getByRole('button', { name: 'Send message' }));
    expect(JSON.parse(vi.mocked(fetch).mock.calls[0][1]!.body as string).payload.text).toBe('compare customers');
    act(() => stream.emit(message)); await user.click(await screen.findByRole('radio', { name: /ABC/ })); await user.click(screen.getByRole('button', { name: 'Confirm selection' }));
    const sent = JSON.parse(vi.mocked(fetch).mock.calls[1][1]!.body as string);
    act(() => { stream.emit({ ...message, messageId: sent.messageId, type: 'component.response', inReplyTo: message.messageId, payload: sent.payload }); stream.emit({ ...message, messageId: 'terminal', type: 'agent.text', inReplyTo: sent.messageId, expectsReply: false, payload: { text: 'Selected customer C101.' } }); });
    expect(await screen.findByText('Selected customer C101.')).toBeInTheDocument(); expect(screen.getByRole('status')).toHaveTextContent('Interaction completed');
  });
});

