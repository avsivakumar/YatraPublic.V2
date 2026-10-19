import { registerTrustedComponents } from './trustedComponents';
import type { ComponentType } from 'react';
import { isComponentCapability, type ComponentCapability } from './componentCapabilities';
import { componentLimits } from './componentLimits';
import type { ComponentLifecycle, ComponentOutcome } from './componentProtocol';
export interface HostedComponentProps {
  instanceId: string;
  title: string;
  description?: string | null;
  props: Record<string, unknown>;
  allowedActions: string[];
  lifecycle: ComponentLifecycle;
  outcome?: ComponentOutcome;
  emitAction: (action: string, data: unknown) => void;
}
export type ValidationResult = { valid: boolean };
export interface ComponentRegistration {
  componentId: string;
  componentVersion: string;
  supportedActions: readonly string[];
  validateProps: (props: unknown) => ValidationResult;
  validateResponse: (action: string, data: unknown, props: unknown) => ValidationResult;
  render: ComponentType<HostedComponentProps>;
}
export class ComponentRegistry {
  private readonly entries = new Map<string, ComponentRegistration>();
  register(entry: ComponentRegistration): void {
    if (!isComponentCapability({ componentId: entry.componentId, componentVersion: entry.componentVersion, supportedActions: entry.supportedActions }))
      throw new Error('Invalid component registration');
    const key = JSON.stringify([entry.componentId, entry.componentVersion]);
    if (this.entries.has(key)) throw new Error('Duplicate component registration');
    if (this.entries.size >= componentLimits.maxCapabilities) throw new Error('Component registry limit reached');
    this.entries.set(key, Object.freeze({ ...entry, supportedActions: Object.freeze([...entry.supportedActions]) }));
  }
  resolve(id: string, version: string): ComponentRegistration | undefined {
    return this.entries.get(JSON.stringify([id, version]));
  }
  getCapabilities(): readonly ComponentCapability[] {
    return Object.freeze([...this.entries.values()].sort((a, b) => a.componentId < b.componentId ? -1 : a.componentId > b.componentId ? 1 : a.componentVersion < b.componentVersion ? -1 : a.componentVersion > b.componentVersion ? 1 : 0)
      .map(entry => Object.freeze({ componentId: entry.componentId, componentVersion: entry.componentVersion, supportedActions: Object.freeze([...entry.supportedActions]) })));
  }
}
// Only statically imported, trusted registrations may be added here.
export const componentRegistry = new ComponentRegistry();

registerTrustedComponents(componentRegistry);

