import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { openMessageStream } from './api';
class Stream extends EventTarget {
  static instances: Stream[] = [];
  onopen: (() => void) | null = null;
  onerror: ((event: Event) => void) | null = null;
  close = vi.fn();
  constructor(public url: string) { super(); Stream.instances.push(this); }
  emit(type: string, value: unknown, id = '') { this.dispatchEvent(new MessageEvent(type, { data: JSON.stringify(value), lastEventId: id })); }
}
beforeEach(() => { vi.useFakeTimers(); Stream.instances = []; vi.stubGlobal('EventSource', Stream); });
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });
describe('component stream recovery', () => {
  it('deduplicates component replay across reconnect and does not advance the cursor for replay markers', () => {
    const receive = vi.fn(); const replay = vi.fn(); const connection = openMessageStream('conversation', receive, vi.fn(), replay);
    const first = Stream.instances[0]; first.onopen?.();
    first.emit('yatra.replay', { messageId: 'marker-start', payload: { active: true } });
    first.emit('component.request', { messageId: 'request', type: 'component.request', payload: {} }, 'request');
    first.emit('component.response', { messageId: 'response', type: 'component.response', inReplyTo: 'request', payload: {} }, 'response');
    first.emit('yatra.replay', { messageId: 'marker-end', payload: { active: false } });
    expect(replay.mock.calls).toEqual([[true], [false]]); expect(receive).toHaveBeenCalledTimes(2);
    first.onerror?.(new Event('error')); vi.advanceTimersByTime(1000);
    const next = Stream.instances[1]; expect(next.url).toContain('lastEventId=response');
    next.emit('component.request', { messageId: 'request', type: 'component.request', payload: {} });
    next.emit('component.response', { messageId: 'response', type: 'component.response', payload: {} });
    first.emit('yatra.replay', { payload: { active: true } });
    expect(receive).toHaveBeenCalledTimes(2); expect(replay).toHaveBeenCalledTimes(2); connection.close();
  });
  it('ignores malformed replay markers without unlocking the UI', () => {
    const replay = vi.fn(); const connection = openMessageStream('conversation', vi.fn(), vi.fn(), replay);
    Stream.instances[0].emit('yatra.replay', { payload: { active: 'false' } });
    expect(replay).not.toHaveBeenCalled(); connection.close();
  });
});
