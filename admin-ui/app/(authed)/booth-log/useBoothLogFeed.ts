"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { usePoll } from "@/lib/use-poll";
import { fetchBoothLogPage, type BoothLogEntry } from "@/lib/booth-log-api";

/** Within the plan's 10-15s guidance for this feed (PLAN T40) — same poll family as Live/
 * Dashboard's 5s (usePoll, unchanged pause/resume/degrade contract), just a slower cadence since
 * the booth log is a narrative history rather than a now-playing readout. */
const BOOTH_LOG_POLL_INTERVAL_MS = 12000;

export interface UseBoothLogFeedResult {
  /** Newest-first: the head page's rows followed by any additionally loaded older pages. `null`
   * until the first head-page poll resolves. */
  entries: BoothLogEntry[] | null;
  /** True when the most recent head-page poll failed; `entries` is left untouched (usePoll's
   * contract — a caller renders a quiet degrade, never discards what's already loaded). */
  error: boolean;
  /** The cursor the next "Load more" click will send. `null` once the oldest page is loaded — hide
   * the load-more control then. */
  nextBefore: string | null;
  /** True while an older page is being fetched. */
  loadingMore: boolean;
  /** True when the last "Load more" attempt failed. The cursor is untouched, so the control stays
   * clickable to retry. */
  loadMoreError: boolean;
  /** Fetches the next older page and appends it. No-op if `nextBefore` is `null` or a fetch is
   * already in flight. */
  loadMore: () => void;
}

/**
 * Booth log feed state (PLAN T40, STORY-195, SPEC F72.2): the newest page comes from the shared
 * `usePoll` hook; any additional older pages loaded via "Load more" are accumulated locally
 * alongside it.
 *
 * **Refresh/paging interaction — deliberately the simpler of PLAN T40's two named options:** on
 * every successful head-page poll, any additionally loaded older pages are dropped and the
 * visible feed collapses back to just the newest page. This is the same full-replace-in-place
 * idiom `usePoll` already uses everywhere else in this codebase (Dashboard, Live) — there is no
 * established merge/prepend-by-cursor pattern here to reuse, and this feed's rows are immutable
 * narrative history (nothing about an already-rendered row ever changes under it), so collapsing
 * is honest rather than lossy: every row is still readable, just via "Load more" again. The
 * alternative (diff the newest cursor and prepend only genuinely-new rows) would preserve an
 * operator's scrolled-back position across a refresh, but there is no precedent for that kind of
 * merge in this codebase to build on, so it is not what's implemented here.
 *
 * A "Load more" fetch that was in flight when a head-page refresh lands is discarded when it
 * resolves (an `epoch` guard) rather than appended onto the now-collapsed list — otherwise a
 * slow older-page response could land after the collapse and reappear detached from the pages
 * that used to sit between it and the head, leaving a gap in the feed.
 */
export function useBoothLogFeed(): UseBoothLogFeedResult {
  const headPoll = usePoll(() => fetchBoothLogPage(), { intervalMs: BOOTH_LOG_POLL_INTERVAL_MS });

  const [olderEntries, setOlderEntries] = useState<BoothLogEntry[]>([]);
  const [olderNextBefore, setOlderNextBefore] = useState<string | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);
  const [loadMoreError, setLoadMoreError] = useState(false);

  // Bumped on every head-page refresh (see doc comment above) so a stale in-flight "Load more"
  // response can recognize it no longer applies and quietly no-op instead of corrupting the list.
  const epochRef = useRef(0);

  useEffect(() => {
    if (headPoll.data === null) return;
    epochRef.current += 1;
    setOlderEntries([]);
    setOlderNextBefore(null);
    setLoadMoreError(false);
    setLoadingMore(false);
  }, [headPoll.data]);

  const nextBefore = olderEntries.length > 0 ? olderNextBefore : (headPoll.data?.nextBefore ?? null);

  const loadMore = useCallback((): void => {
    if (loadingMore || nextBefore === null) return;

    const epoch = epochRef.current;
    setLoadingMore(true);
    setLoadMoreError(false);

    void fetchBoothLogPage(nextBefore)
      .then((page) => {
        if (epoch !== epochRef.current) return;
        setOlderEntries((prev) => [...prev, ...page.entries]);
        setOlderNextBefore(page.nextBefore);
      })
      .catch(() => {
        if (epoch !== epochRef.current) return;
        setLoadMoreError(true);
      })
      .finally(() => {
        if (epoch !== epochRef.current) return;
        setLoadingMore(false);
      });
  }, [loadingMore, nextBefore]);

  const entries = headPoll.data === null ? null : [...headPoll.data.entries, ...olderEntries];

  return {
    entries,
    error: headPoll.error,
    nextBefore,
    loadingMore,
    loadMoreError,
    loadMore,
  };
}
