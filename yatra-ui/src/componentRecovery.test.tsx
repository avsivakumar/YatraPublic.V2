import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ComponentHost } from './components/ComponentHost';
import { componentStorageKey, fingerprint } from './componentState';
import type { ComponentRequestPayload, YatraMessage } from './types';
const request: ComponentRequestPayload = { componentId: 'customer-comparison', componentVersion: '1.0', instanceId: '01K00000000000000000000001', title: 'Recover interaction', props: { customers: [{ id: 'C101', name: 'One', status: 'Active' }, { id: 'C205', name: 'Two', status: 'Pending' }] }, allowedActions: ['select', 'cancel'] };
const message: YatraMessage = { messageId: '01K00000000000000000000002', conversationId: '01K00000000000000000000000', type: 'component.request', source: 'sample', destination: 'ui', createdAtUtc: '', expectsReply: true, payload: request };
const key = `yatra:${componentStorageKey(message)}`;
const read = () => JSON.parse(localStorage.getItem(key)!);
const ack = (id: string) => new Response(JSON.stringify({ messageId: id, status: 'accepted', replayed: false }), { status: 202 });
function sent(index = 0) { return JSON.parse(vi.mocked(fetch).mock.calls[index][1]!.body as string); }
function submit() { fireEvent.click(screen.getByRole('radio', { name: /One/ })); fireEvent.click(screen.getByRole('button', { name: 'Confirm selection' })); }
function outcome(id: string, type = 'agent.text'): YatraMessage { return { ...message, messageId: '01K00000000000000000000004', type, inReplyTo: id, payload: type === 'error' ? { code: 'failed', message: 'Failed', retryable: false } : { text: 'Done' } }; }
beforeEach(() => { localStorage.clear(); vi.stubGlobal('fetch', vi.fn(async (_url, init) => ack(JSON.parse(init.body).messageId))); });
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });
describe('component durable browser recovery', () => {
  it('reloads an untouched request as ready', () => {
    const view = render(<ComponentHost payload={request} message={message} />); view.unmount();
    render(<ComponentHost payload={request} message={message} />);
    expect(screen.getByRole('button', { name: 'Cancel comparison' })).toBeEnabled(); expect(fetch).not.toHaveBeenCalled();
  });
  it('saves the immutable identity and exact JSON before network submission', async () => {
    vi.mocked(fetch).mockImplementation(async (_url, init) => { expect(read().submission).toBe(init!.body); expect(read().lifecycle).toBe('submitting'); return ack(JSON.parse(init!.body as string).messageId); });
    render(<ComponentHost payload={request} message={message} />); submit();
    await waitFor(() => expect(read().acknowledged).toBe(true));
    expect(read().lifecycle).toBe('submitted'); expect(read().submission).toBe(vi.mocked(fetch).mock.calls[0][1]!.body);
  });
  it('restores submission interrupted by reload and retries exactly the original identity/payload', async () => {
    let late: (value: Response) => void = () => undefined;
    vi.mocked(fetch).mockImplementationOnce(() => new Promise(resolve => { late = resolve; }));
    const view = render(<ComponentHost payload={request} message={message} />); submit(); const original = sent(); view.unmount();
    render(<ComponentHost payload={request} message={message} />);
    expect(screen.getByRole('status')).toHaveTextContent('Submission could not be confirmed');
    fireEvent.click(screen.getByRole('button', { name: 'Retry response' }));
    await waitFor(() => expect(read().acknowledged).toBe(true)); expect(sent(1)).toEqual(original);
    expect(vi.mocked(fetch).mock.calls[1][1]!.body).toBe(vi.mocked(fetch).mock.calls[0][1]!.body);
    await act(async () => late(ack(original.messageId)));
    expect(read().lifecycle).toBe('submitted');
  });
  it('keeps the same response after an acceptance timeout, remount and retry', async () => {
    vi.mocked(fetch).mockRejectedValueOnce(new DOMException('Timed out', 'TimeoutError'));
    const view = render(<ComponentHost payload={request} message={message} />); submit();
    await screen.findByRole('button', { name: 'Retry response' }); const firstBody = vi.mocked(fetch).mock.calls[0][1]!.body;
    view.unmount(); render(<ComponentHost payload={request} message={message} />); fireEvent.click(screen.getByRole('button', { name: 'Retry response' }));
    await waitFor(() => expect(read().lifecycle).toBe('submitted')); expect(vi.mocked(fetch).mock.calls[1][1]!.body).toBe(firstBody);
  });
  it('restores acknowledged state without unlocking or resubmitting', async () => {
    const view = render(<ComponentHost payload={request} message={message} />); submit();
    await waitFor(() => expect(read().acknowledged).toBe(true)); view.unmount(); render(<ComponentHost payload={request} message={message} />);
    expect(screen.getByRole('status')).toHaveTextContent('Waiting for confirmation'); expect(screen.getByRole('button', { name: 'Cancel comparison' })).toBeDisabled(); expect(fetch).toHaveBeenCalledTimes(1);
  });
  it.each(['agent.text', 'error'])('keeps an early %s outcome across late HTTP success and reload', async type => {
    let resolve: (value: Response) => void = () => undefined; vi.mocked(fetch).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const view = render(<ComponentHost payload={request} message={message} />); submit(); const response = sent();
    view.rerender(<ComponentHost payload={request} message={message} messages={[outcome(response.messageId, type)]} />);
    await waitFor(() => expect(read().lifecycle).toBe(type === 'agent.text' ? 'completed' : 'failed'));
    await act(async () => resolve(ack(response.messageId)));
    expect(read().lifecycle).toBe(type === 'agent.text' ? 'completed' : 'failed'); view.unmount();
    render(<ComponentHost payload={request} message={message} />);
    expect(screen.getByRole('button', { name: 'Cancel comparison' })).toBeDisabled(); expect(read().acknowledged).toBe(true);
  });
  it('does not let an old unmounted HTTP attempt overwrite a new host terminal result', async () => {
    let resolve: (value: Response) => void = () => undefined; vi.mocked(fetch).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const first = render(<ComponentHost payload={request} message={message} />); submit(); const response = sent(); first.unmount();
    render(<ComponentHost payload={request} message={message} messages={[outcome(response.messageId)]} />);
    await waitFor(() => expect(read().lifecycle).toBe('completed')); await act(async () => resolve(ack(response.messageId))); expect(read().lifecycle).toBe('completed');
  });
  it.each(['select', 'cancel'])('restores terminal %s from replay with no local state and ignores duplicates', async action => {
    const response = { ...message, messageId: '01K00000000000000000000003', type: 'component.response', inReplyTo: message.messageId,
      payload: { componentId: request.componentId, componentVersion: request.componentVersion, instanceId: request.instanceId, action, data: action === 'select' ? { customerId: 'C101' } : {} } };
    const history = [message, response, outcome(response.messageId)];
    const view = render(<ComponentHost payload={request} message={message} messages={history} />);
    await waitFor(() => expect(read().lifecycle).toBe(action === 'select' ? 'completed' : 'cancelled'));
    view.rerender(<ComponentHost payload={request} message={message} messages={[...history, ...history]} />);
    expect(screen.getAllByRole('status')).toHaveLength(1); expect(screen.getByRole('status')).toHaveTextContent(action === 'select' ? 'Interaction completed.' : 'Interaction cancelled.'); expect(fetch).not.toHaveBeenCalled();
    view.unmount(); render(<ComponentHost payload={request} message={message} />); expect(screen.getByRole('button', { name: 'Cancel comparison' })).toBeDisabled();
  });
  it('restores expiry delivered as a durable request-correlated status', async () => {
    const expiry = { ...message, type: 'status', messageId: 'expiry', inReplyTo: message.messageId, payload: { componentLifecycle: 'expired' } };
    const view = render(<ComponentHost payload={request} message={message} messages={[expiry]} />);
    await waitFor(() => expect(read().lifecycle).toBe('expired')); view.unmount(); render(<ComponentHost payload={request} message={message} />);
    expect(screen.getByRole('status')).toHaveTextContent('expired'); expect(screen.getByRole('button', { name: 'Cancel comparison' })).toBeDisabled();
  });
  it('fails closed on changed data under the same request ID but accepts a fresh request/instance', async () => {
    const view = render(<ComponentHost payload={request} message={message} />); submit(); await waitFor(() => expect(read().acknowledged).toBe(true));
    view.rerender(<ComponentHost payload={{ ...request, title: 'Changed request' }} message={message} />);
    expect(screen.getByRole('status')).toHaveTextContent('could not be completed'); expect(screen.getByRole('button', { name: 'Cancel comparison' })).toBeDisabled();
    const fresh = { ...request, instanceId: '01K00000000000000000000009' };
    view.rerender(<ComponentHost payload={fresh} message={{ ...message, messageId: '01K00000000000000000000008', payload: fresh }} />);
    expect(screen.getByRole('button', { name: 'Cancel comparison' })).toBeEnabled(); expect(screen.getByRole('radio', { name: /One/ })).not.toBeChecked();
  });
  it.each(['{broken', 'null', JSON.stringify({ version: 9 }), JSON.stringify({ version: 1, request: fingerprint(request), lifecycle: 'submitted', acknowledged: true }),
    JSON.stringify({ version: 1, request: fingerprint(request), lifecycle: 'ready', acknowledged: true }), JSON.stringify({ version: 1, request: fingerprint(request), lifecycle: 'retryable', acknowledged: false, submission: 'bad' })])('contains corrupt/unsupported persisted state: %s', raw => {
    localStorage.setItem(key, raw); render(<ComponentHost payload={request} message={message} />);
    expect(screen.getByRole('button', { name: 'Cancel comparison' })).toBeDisabled(); expect(screen.getByRole('status')).toHaveTextContent('could not be completed'); expect(fetch).not.toHaveBeenCalled();
  });
  it('does not submit if durable identity storage fails', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('Quota exceeded'); });
    render(<ComponentHost payload={request} message={message} />); submit(); expect(fetch).not.toHaveBeenCalled(); expect(screen.getByRole('alert')).toHaveTextContent('could not be saved');
  });
  it('locks actions until replay is complete', () => {
    const view = render(<ComponentHost payload={request} message={message} replaying />); expect(screen.getByRole('button', { name: 'Cancel comparison' })).toBeDisabled();
    expect(screen.getByRole('status')).toHaveTextContent('Restoring'); view.rerender(<ComponentHost payload={request} message={message} replaying={false} />);
    expect(screen.getByRole('button', { name: 'Cancel comparison' })).toBeEnabled();
  });
});
