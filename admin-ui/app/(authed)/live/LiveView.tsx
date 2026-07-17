"use client";

import type { ReactNode } from "react";
import { usePoll } from "@/lib/use-poll";
import { fetchNowPlaying, fetchPlayHistory, isCatalogMediaId } from "@/lib/broadcast-api";
import { NowPlayingCard } from "../_components/NowPlayingCard";
import { RatingControls, type RatingControlsValue } from "../_components/RatingControls";
import { PlayHistoryTable } from "./PlayHistoryTable";
import { DEFAULT_RATING, useLiveRatings } from "./useLiveRatings";

interface LiveViewProps {
  /** Test-only injection point, threaded to the clock formatter; production omits this and gets the browser's local zone. */
  timeZone?: string;
}

/**
 * The Live page's content (SPEC F28.7–F28.8, STORY-088): the full on-air
 * view — the same now-playing hero as the Dashboard (double-ring card,
 * dial-marking progress, ON AIR badge) plus the complete play-history ring,
 * newest first. Polls through the shared `usePoll` hook (Q5, unchanged: 5 s
 * cadence, Page Visibility pause/resume, quiet per-card degrade on fetch
 * failure, no toasts). Purely client-driven — there is no SSR-prefetched
 * initial state, so every card starts in its loading/skeleton state until
 * its first poll resolves, matching the Dashboard's pattern.
 *
 * Track rating (SPEC F33.11, STORY-114): the now-playing card and every
 * catalog-id history row get a score chip + vote/never-play controls, this
 * page only — the Dashboard's shared `NowPlayingCard`/`RecentPlays` stay
 * read-only (F33.12) because this page is the only caller that supplies the
 * card's opt-in `ratingControls` slot. Ratings compose from a THIRD,
 * independent poller (`useLiveRatings`, same F16.6-style cadence) derived
 * from the ids currently visible here — `fetchNowPlaying`/`fetchPlayHistory`
 * calls above are untouched.
 */
export function LiveView({ timeZone }: LiveViewProps = {}): ReactNode {
  const nowPlaying = usePoll(fetchNowPlaying);
  const playHistory = usePoll(fetchPlayHistory);

  const nowPlayingMediaId =
    nowPlaying.data?.kind === "track" && isCatalogMediaId(nowPlaying.data.mediaId)
      ? nowPlaying.data.mediaId
      : null;
  const historyMediaIds = playHistory.data?.map((entry) => entry.mediaId) ?? [];
  const visibleIds = nowPlayingMediaId !== null ? [nowPlayingMediaId, ...historyMediaIds] : historyMediaIds;

  const { ratings, applyRating } = useLiveRatings(visibleIds);

  const nowPlayingRatingControls =
    nowPlayingMediaId !== null ? (
      <RatingControls
        mediaId={nowPlayingMediaId}
        value={ratings.get(nowPlayingMediaId) ?? DEFAULT_RATING}
        onChange={(next: RatingControlsValue) => applyRating({ mediaId: nowPlayingMediaId, ...next })}
      />
    ) : undefined;

  return (
    <div className="space-y-6">
      <NowPlayingCard
        state={nowPlaying.data}
        error={nowPlaying.error}
        ratingControls={nowPlayingRatingControls}
      />
      <PlayHistoryTable
        entries={playHistory.data}
        error={playHistory.error}
        timeZone={timeZone}
        ratings={ratings}
        onRatingChange={applyRating}
      />
    </div>
  );
}
