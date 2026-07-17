"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { usePoll } from "@/lib/use-poll";
import { fetchRatings, type RatingEntry } from "@/lib/broadcast-api";

/** Same cadence the Live page's other two pollers already use (F16.6) — an independent poller,
 * not a shared tick, matching how now-playing/play-history already poll independently of each
 * other. */
const RATINGS_POLL_INTERVAL_MS = 5000;

/** F33.2's ledger default for a media id with no `library.media_rating` row. */
export const DEFAULT_RATING: Readonly<Pick<RatingEntry, "score" | "neverPlay">> = {
  score: 50,
  neverPlay: false,
};

export interface UseLiveRatingsResult {
  /** Current rating per catalog mediaId. An id absent from the map hasn't resolved from a poll
   * yet — callers render {@link DEFAULT_RATING} for it rather than wait. */
  ratings: ReadonlyMap<string, RatingEntry>;
  /** Folds a vote/never-play response back into the map immediately (no refetch, F33.11) and
   * marks the id as just-written so the next poll tick can't clobber it with a stale read. */
  applyRating: (entry: RatingEntry) => void;
}

/**
 * Composes `GET /api/ratings?ids=…` on the Q5 poll cadence for whatever catalog ids are visible
 * on the Live page (now-playing + history) — independent of, and without altering, the
 * now-playing/play-history pollers themselves (F33.9, F16.6).
 *
 * **Stale-poll-vs-vote ordering decision (STORY-114):** a vote/never-play response is always at
 * least as fresh as a same-tick poll read for the same id — the poll's request could have been
 * in flight *before* the write landed, so there's no reliable way to compare "freshness" between
 * two independent endpoints by timestamp alone. Rather than thread response timestamps through
 * two unrelated wire calls, this hook takes the simplest rule that is still correct: after a
 * write, the written id is held authoritative for one full poll interval. Any poll response for
 * that id arriving inside that window is dropped; the following tick's read is trusted normally.
 * One interval is enough for a poll request that raced the write to resolve and be discarded.
 */
export function useLiveRatings(ids: readonly string[]): UseLiveRatingsResult {
  const [ratings, setRatings] = useState<Map<string, RatingEntry>>(new Map());
  const recentWritesRef = useRef<Map<string, number>>(new Map());

  // `ids` is a fresh array literal from the caller on every render (derived from the
  // now-playing/history poll state), so a `useCallback` here would never actually memoize
  // anything — `usePoll` already re-reads the freshest fetcher closure via its own ref on every
  // render regardless of identity, so a plain inline closure is the honest way to write this.
  const poll = usePoll<RatingEntry[]>(() => fetchRatings(ids), {
    intervalMs: RATINGS_POLL_INTERVAL_MS,
  });

  // usePoll's mount-time fetch fires before now-playing/play-history have resolved, so its very
  // first call sees an empty `ids` — and usePoll otherwise only re-fires on its fixed interval,
  // not when the fetcher's closure changes. Without this, a freshly-visible id (the moment
  // now-playing/history first resolve, or a new track enters the ring) would wait up to one full
  // poll interval before its rating ever loads. `refresh()` re-runs the fetcher immediately,
  // outside the regular cadence, whenever the *set* of visible ids actually changes; the initial
  // mount's own call is skipped here since usePoll already made it.
  const idsKey = [...new Set(ids)].sort().join(",");
  const isFirstIdsChange = useRef(true);
  useEffect(() => {
    if (isFirstIdsChange.current) {
      isFirstIdsChange.current = false;
      return;
    }
    poll.refresh();
    // Deliberately keyed on `idsKey` alone, not `poll`/`poll.refresh` — `usePoll` rebuilds its
    // returned object every render, but `refresh` itself is a stable `useCallback` underneath, so
    // depending on the whole object would re-run this on every render instead of only on an
    // actual id-set change.
  }, [idsKey]);

  useEffect(() => {
    const freshRatings = poll.data;
    if (freshRatings === null) return;
    const now = Date.now();
    setRatings((prev) => {
      const next = new Map(prev);
      for (const entry of freshRatings) {
        const writtenAt = recentWritesRef.current.get(entry.mediaId);
        if (writtenAt !== undefined && now - writtenAt < RATINGS_POLL_INTERVAL_MS) {
          continue;
        }
        next.set(entry.mediaId, entry);
      }
      return next;
    });
  }, [poll.data]);

  const applyRating = useCallback((entry: RatingEntry) => {
    recentWritesRef.current.set(entry.mediaId, Date.now());
    setRatings((prev) => {
      const next = new Map(prev);
      next.set(entry.mediaId, entry);
      return next;
    });
  }, []);

  return { ratings, applyRating };
}
