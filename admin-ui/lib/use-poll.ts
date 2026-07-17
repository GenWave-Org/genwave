"use client";

import { useCallback, useEffect, useRef, useState } from "react";

const DEFAULT_INTERVAL_MS = 5000;

export interface UsePollOptions {
  /** Poll cadence in milliseconds while the tab is visible. Default 5000 (SPEC F28.8). */
  intervalMs?: number;
}

export interface UsePollResult<T> {
  /** Last successfully fetched value, or null before the first success. */
  data: T | null;
  /**
   * True when the most recent poll failed. `data` is left untouched — a
   * failure after a success keeps the stale value visible, and the next
   * successful poll clears this flag. Callers render a quiet inline
   * "unavailable" hint on the affected card; never a toast (SPEC F28.8,
   * STORY-087 AC5).
   */
  error: boolean;
  /** Fires the fetcher immediately, outside the regular cadence. */
  refresh: () => void;
}

/**
 * Shared polling hook for live dashboard/live-page data (SPEC F28.7–F28.8).
 *
 * - Fires `fetcher` immediately on mount, then every `intervalMs`.
 * - Updates `data` in place — consumers never unmount/remount on a poll.
 * - Pauses while `document.hidden` is true (Page Visibility) and resumes
 *   with an immediate fetch on visibility.
 * - A rejected `fetcher` sets `error` without discarding the last-known
 *   `data`; a subsequent success clears `error`. Never throws past this
 *   hook and never surfaces a toast — that's the caller's call to make
 *   quietly, per card.
 *
 * Exported for reuse by the Live page (Q6) as well as the Dashboard (Q5).
 */
export function usePoll<T>(
  fetcher: () => Promise<T>,
  { intervalMs = DEFAULT_INTERVAL_MS }: UsePollOptions = {}
): UsePollResult<T> {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState(false);

  // The fetcher closure is re-created on every render by most callers
  // (inline arrow functions); stash it in a ref so the effect below can
  // depend only on `intervalMs` and stay mounted for the component's
  // lifetime instead of tearing down/rebuilding the interval and
  // visibility listener on every render.
  const fetcherRef = useRef(fetcher);
  fetcherRef.current = fetcher;

  const poll = useCallback(async (): Promise<void> => {
    try {
      const result = await fetcherRef.current();
      setData(result);
      setError(false);
    } catch {
      setError(true);
    }
  }, []);

  useEffect(() => {
    let intervalId: ReturnType<typeof setInterval> | null = null;

    const startInterval = (): void => {
      intervalId = setInterval(() => {
        void poll();
      }, intervalMs);
    };

    void poll();
    startInterval();

    const handleVisibilityChange = (): void => {
      if (document.hidden) {
        if (intervalId !== null) {
          clearInterval(intervalId);
          intervalId = null;
        }
        return;
      }
      void poll();
      startInterval();
    };

    document.addEventListener("visibilitychange", handleVisibilityChange);

    return () => {
      if (intervalId !== null) {
        clearInterval(intervalId);
      }
      document.removeEventListener("visibilitychange", handleVisibilityChange);
    };
  }, [poll, intervalMs]);

  const refresh = useCallback((): void => {
    void poll();
  }, [poll]);

  return { data, error, refresh };
}
