import { withinComponentLimits } from './componentLimits';

export interface ComponentRequestPayload {
  componentId: string;
  componentVersion: string;
  instanceId: string;
  title: string;
  description?: string | null;
  props: Record<string, unknown>;
  allowedActions: string[];
}
export interface ComponentResponsePayload {
  componentId: string;
  componentVersion: string;
  instanceId: string;
  action: string;
  data: Record<string, unknown>;
}
export type ComponentLifecycle = 'ready' | 'submitting' | 'retryable' | 'submitted' | 'completed' | 'cancelled' | 'expired' | 'failed' | 'unsupported';
export interface ComponentOutcome { status: ComponentLifecycle; message?: string; action?: string; data?: Record<string, unknown> }
const identifier = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/;
const version = /^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$/;
const ulid = /^[0-7][0-9A-HJKMNP-TV-Z]{25}$/i;
export function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function matches(value: unknown, pattern: RegExp, max: number): value is string {
  return typeof value === 'string' && value.length <= max && pattern.exec(value)?.[0] === value;
}
export function isComponentIdentity(value: unknown): value is Record<string, unknown> & Pick<ComponentRequestPayload, 'componentId' | 'componentVersion' | 'instanceId'> {
  return isObject(value) && matches(value.componentId, identifier, 100) && matches(value.componentVersion, version, 32) && matches(value.instanceId, ulid, 26);
}
export function isAction(value: unknown): value is string { return matches(value, identifier, 64); }
export function isComponentId(value: unknown): value is string { return matches(value, identifier, 100); }
export function isComponentVersion(value: unknown): value is string { return matches(value, version, 32); }
export function isComponentRequest(value: unknown): value is ComponentRequestPayload {
  return withinComponentLimits(value) && isComponentIdentity(value) && isObject(value) && typeof value.title === 'string' && value.title.trim().length > 0 && value.title.length <= 200 &&
    (value.description == null || (typeof value.description === 'string' && value.description.length <= 2000)) && isObject(value.props) &&
    Array.isArray(value.allowedActions) && value.allowedActions.length > 0 && value.allowedActions.length <= 16 && value.allowedActions.every(isAction) && new Set(value.allowedActions).size === value.allowedActions.length;
}
export function isComponentResponse(value: unknown): value is ComponentResponsePayload {
  return withinComponentLimits(value) && isComponentIdentity(value) && isObject(value) && isAction(value.action) && isObject(value.data);
}


