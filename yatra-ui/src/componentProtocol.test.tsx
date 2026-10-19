import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ComponentRegistry } from './componentRegistry';
import { isComponentRequest, isComponentResponse } from './componentProtocol';
import { ComponentHost } from './components/ComponentHost';
import { MessageList } from './components/MessageList';
const request = { componentId: 'test-card', componentVersion: '1.0', instanceId: '01K00000000000000000000000', title: 'Test', props: {}, allowedActions: ['select'] };
function registry(validateProps = (_: unknown) => ({ valid: true })) {
  const result = new ComponentRegistry();
  result.register({ componentId: 'test-card', componentVersion: '1.0', supportedActions: ['select'], validateProps, validateResponse: () => ({ valid: true }), render: ({ title, lifecycle }) => <div>{title}: {lifecycle}</div> });
  return result;
}
describe('component protocol foundation', () => {
  it('accepts request and response JSON round trips', () => {
    expect(isComponentRequest(JSON.parse(JSON.stringify(request)))).toBe(true);
    expect(isComponentResponse(JSON.parse(JSON.stringify({ ...request, action: 'select', data: {} })))).toBe(true);
  });
  it.each(['componentId', 'componentVersion', 'instanceId', 'title', 'props', 'allowedActions'])('rejects missing %s', field => {
    const value: Record<string, unknown> = { ...request }; delete value[field]; expect(isComponentRequest(value)).toBe(false);
  });
  it.each([{ componentId: 'Bad ID' }, { componentVersion: 'latest' }, { instanceId: 'invalid' }, { allowedActions: [] }, { allowedActions: ['Select'] }, { allowedActions: ['select', 'select'] }, { props: [] }, { title: ' ' }, { description: 42 }])('rejects malformed fields %j', override => {
    expect(isComponentRequest({ ...request, ...override })).toBe(false);
  });
  it.each([null, [], {}, { ...request, action: 'Select', data: {} }, { ...request, action: 'select', data: null }, { ...request, action: 'select' }])('rejects malformed responses %j', value => expect(isComponentResponse(value)).toBe(false));
  it('resolves exact versions and rejects duplicate registrations', () => {
    const entries = registry(); const entry = entries.resolve('test-card', '1.0')!;
    expect(entries.resolve('test-card', '1.1')).toBeUndefined();
    expect(entries.resolve('Test-card', '1.0')).toBeUndefined();
    expect(() => entries.register(entry)).toThrow('Duplicate');
    entries.register({ ...entry, componentVersion: '2.0' });
    expect(entries.resolve('test-card', '2.0')).toBeDefined();
  });
  it('hosts a trusted registration with actions disabled in Phase 1', () => {
    render(<ComponentHost payload={request} registry={registry()} />);
    expect(screen.getByText('Test: unsupported')).toBeInTheDocument();
  });
  it.each([{ ...request, componentId: 'unknown' }, { ...request, componentVersion: '2.0' }, null, { ...request, allowedActions: ['cancel'] }])('renders unsupported payload safely %j', payload => {
    render(<ComponentHost payload={payload} registry={registry()} />);
    expect(screen.getByRole('status')).toHaveTextContent('Component unavailable');
  });
  it.each([() => ({ valid: false }), () => { throw new Error('private details'); }])('contains invalid properties and validator errors', validator => {
    render(<ComponentHost payload={request} registry={registry(validator)} />);
    expect(screen.getByRole('status')).toHaveTextContent('Component unavailable');
    expect(screen.queryByText('private details')).not.toBeInTheDocument();
  });
  it('contains trusted rendering failures', () => {
    const entries = registry();
    entries.register({ ...entries.resolve('test-card', '1.0')!, componentVersion: '2.0', render: () => { throw new Error('private details'); } });
    render(<ComponentHost payload={{ ...request, componentVersion: '2.0' }} registry={entries} />);
    expect(screen.getByRole('status')).toHaveTextContent('Component unavailable');
  });
  it('keeps the timeline working around malformed component requests', () => {
    const base = { messageId: 'a', conversationId: 'b', source: 'sample', destination: 'ui', createdAtUtc: '', expectsReply: true };
    render(<MessageList messages={[{ ...base, type: 'component.request', payload: null }, { ...base, messageId: 'c', type: 'agent.text', payload: { text: 'Still working' } }]} renderForm={() => null} />);
    expect(screen.getByRole('status')).toHaveTextContent('Component unavailable');
    expect(screen.getByText('Still working')).toBeInTheDocument();
  });
});
