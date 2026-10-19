export const componentLimits = Object.freeze({
  maxUtf8Bytes: 65_536, maxDepth: 8, maxNodes: 2_048, maxArrayItems: 100,
  maxObjectProperties: 100, maxPropertyNameLength: 100, maxStringLength: 4_096,
  maxHostedComponents: 20, maxCapabilities: 32,
});

// Check shape before stringification so excessive nesting or cycles cannot crash rendering.
export function withinComponentLimits(value: unknown): boolean {
  let nodes = 0;
  const ancestors = new Set<object>();
  function visit(item: unknown, depth: number): boolean {
    if (depth > componentLimits.maxDepth || ++nodes > componentLimits.maxNodes) return false;
    if (item === null || typeof item === 'boolean') return true;
    if (typeof item === 'number') return Number.isFinite(item);
    if (typeof item === 'string') return item.length <= componentLimits.maxStringLength;
    if (typeof item !== 'object' || ancestors.has(item)) return false;
    ancestors.add(item);
    let valid: boolean;
    if (Array.isArray(item)) valid = item.length <= componentLimits.maxArrayItems && item.every(child => visit(child, depth + 1));
    else {
      const entries = Object.entries(item);
      valid = entries.length <= componentLimits.maxObjectProperties && entries.every(([key, child]) => key.length <= componentLimits.maxPropertyNameLength && visit(child, depth + 1));
    }
    ancestors.delete(item);
    return valid;
  }
  try { return visit(value, 0) && new TextEncoder().encode(JSON.stringify(value)).length <= componentLimits.maxUtf8Bytes; }
  catch { return false; }
}
