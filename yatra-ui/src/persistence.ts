export function readSaved<T>(key: string, fallback: T): T {
  try {
    const raw = localStorage.getItem(`yatra:${key}`);
    if (raw === null) return fallback;
    const value: unknown = JSON.parse(raw);
    if (value === null || typeof value !== typeof fallback || Array.isArray(value) !== Array.isArray(fallback)) return fallback;
    return value as T;
  } catch {
    return fallback;
  }
}

export function saveState(key: string, value: unknown): boolean {
  try {
    localStorage.setItem(`yatra:${key}`, JSON.stringify(value));
    return true;
  } catch {
    console.warn('Yatra could not persist browser state.');
    return false;
  }
}

// Unlike readSaved, recovery callers must distinguish missing state from corrupt state.
export function readStoredState(key: string): { kind: 'missing' } | { kind: 'invalid' } | { kind: 'saved'; value: unknown } {
  try {
    const raw = localStorage.getItem(`yatra:${key}`);
    return raw === null ? { kind: 'missing' } : { kind: 'saved', value: JSON.parse(raw) };
  } catch { return { kind: 'invalid' }; }
}

