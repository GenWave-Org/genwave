"use client";

import type { ReactNode } from "react";
import { usePoll } from "@/lib/use-poll";
import { fetchNowPlaying, fetchPlayHistory, isCatalogMediaId } from "@/lib/broadcast-api";
import { personaNameOrFallback, usePersonaDirectory } from "@/lib/use-persona-directory";
import { NowPlayingCard } from "../_components/NowPlayingCard";
import { PersonaTasteThumbs } from "../_components/PersonaTasteThumbs";
import { RatingControls, type RatingControlsValue } from "../_components/RatingControls";
import { PlayHistoryTable } from "./PlayHistoryTable";
import { DEFAULT_RATING, useLiveRatings } from "./useLiveRatings";
import { useNowPlayingTasteAttribution } from "./useNowPlayingTasteAttribution";

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
 *
 * Persona taste (SPEC F84.1, F84.6-F84.7; STORY-215): the now-playing card also gets an opt-in
 * `tasteThumbControls` slot, resolved from TWO more independent sources — `useNowPlayingTasteAttribution`
 * (which booth-log row/persona this moment's thumb credits, per that hook's own doc comment) and
 * `usePersonaDirectory` (persona id -> name, one fetch per mount). A persona-less/predating-the-
 * column airing resolves to no attribution at all, so no control renders (F84.6) — this page never
 * falls back to "whichever persona is active now". `key={attribution.boothLogRowId}` forces the
 * control to remount (and its own settled-direction state to reset) whenever a new track's row
 * swaps in, rather than inheriting the previous track's disabled buttons.
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

  const personaDirectory = usePersonaDirectory();
  const tasteAttribution = useNowPlayingTasteAttribution();

  const nowPlayingTasteThumbControls =
    nowPlaying.data?.kind === "track" && tasteAttribution !== null ? (
      <PersonaTasteThumbs
        key={tasteAttribution.boothLogRowId}
        boothLogRowId={tasteAttribution.boothLogRowId}
        personaName={personaNameOrFallback(personaDirectory, tasteAttribution.personaId)}
      />
    ) : undefined;

  return (
    <div className="space-y-6">
      <NowPlayingCard
        state={nowPlaying.data}
        error={nowPlaying.error}
        ratingControls={nowPlayingRatingControls}
        tasteThumbControls={nowPlayingTasteThumbControls}
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
