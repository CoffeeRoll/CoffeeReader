import { useCallback, useEffect, useState } from "react";

/** Reads a stored value, tolerating private mode, quota errors and malformed data. */
export function readStored<T>(key: string, fallback: T): T {
  try {
    const raw = window.localStorage.getItem(key);
    return raw === null ? fallback : { ...fallbackObject(fallback), ...JSON.parse(raw) };
  } catch {
    return fallback;
  }
}

function fallbackObject<T>(fallback: T): T | object {
  return typeof fallback === "object" && fallback !== null && !Array.isArray(fallback) ? fallback : {};
}

export function writeStored<T>(key: string, value: T) {
  try {
    window.localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // Storage unavailable; the preference just won't persist.
  }
}

/** Per-device preferences (layout, reader typography). */
export function useLocalStorage<T>(key: string, fallback: T): [T, (value: T | ((current: T) => T)) => void] {
  const [value, setValue] = useState<T>(() => {
    try {
      const raw = window.localStorage.getItem(key);
      if (raw === null) {
        return fallback;
      }

      const parsed = JSON.parse(raw);
      return typeof fallback === "object" && fallback !== null && !Array.isArray(fallback) ? { ...fallback, ...parsed } : parsed;
    } catch {
      return fallback;
    }
  });

  useEffect(() => writeStored(key, value), [key, value]);

  const update = useCallback((next: T | ((current: T) => T)) => setValue(next), []);
  return [value, update];
}
