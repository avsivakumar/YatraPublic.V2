import { componentLimits, withinComponentLimits } from './componentLimits';
import { isAction, isComponentId, isComponentVersion, isObject } from './componentProtocol';

export interface ComponentCapability {
  readonly componentId: string;
  readonly componentVersion: string;
  readonly supportedActions: readonly string[];
}
export function isComponentCapability(value: unknown): value is ComponentCapability {
  return isObject(value) && Object.keys(value).length === 3 && isComponentId(value.componentId) && isComponentVersion(value.componentVersion)
    && Array.isArray(value.supportedActions) && value.supportedActions.length > 0 && value.supportedActions.length <= 16
    && value.supportedActions.every(isAction) && new Set(value.supportedActions).size === value.supportedActions.length;
}
export function isComponentCapabilities(value: unknown): value is ComponentCapability[] {
  if (!Array.isArray(value) || value.length > componentLimits.maxCapabilities || !withinComponentLimits(value) || !value.every(isComponentCapability)) return false;
  return new Set(value.map(entry => JSON.stringify([entry.componentId, entry.componentVersion]))).size === value.length;
}
