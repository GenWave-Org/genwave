"use client";

import { useEffect, useState } from "react";

/**
 * Client-only state persisted to `localStorage` under `key` (SPEC F49.3 — the column-visibility
 * toggle is the first consumer). Mirrors ThemeToggle's cookie-backed "hydrate after mount" shape
 * (SPEC F28.4): the first render always matches the server's markup (`defaultValue`), then an
 * effect swaps in whatever's actually stored, so there is no hydration mismatch.
 *
 * `parse` validates the raw `JSON.parse()` result before it's trusted: `localStorage` is
 * operator-writable and can carry a foreign/corrupt value under this key (a value from a future
 * release, hand-edited devtools, a key collision), so it is boundary data, not typed data — it is
 * never cast with `as T`. Return `null` from `parse` to reject the stored value and keep
 * `defaultValue`.
 */
export function usePersistedState<T>(
  key: string,
  defaultValue: T,
  parse: (raw: unknown) => T | null
): [T, (next: T) => void] {
  const [value, setValue] = useState<T>(defaultValue);

  // Runs once at mount, same as ThemeToggle's resolveCurrentTheme() effect — `key`/`parse` are
  // expected to be stable for the lifetime of the component that calls this hook.
  useEffect(() => {
    const raw = window.localStorage.getItem(key);
    if (raw === null) return;

    let parsedJson: unknown;
    try {
      parsedJson = JSON.parse(raw);
    } catch {
      return;
    }

    const parsed = parse(parsedJson);
    if (parsed !== null) setValue(parsed);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function update(next: T): void {
    setValue(next);
    window.localStorage.setItem(key, JSON.stringify(next));
  }

  return [value, update];
}
