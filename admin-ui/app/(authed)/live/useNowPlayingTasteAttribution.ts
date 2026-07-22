"use client";

import { useMemo } from "react";
import { usePoll } from "@/lib/use-poll";
import { fetchBoothLogPage, type BoothLogPage } from "@/lib/booth-log-api";

/** Same F16.6-style independent-poller cadence as `useLiveRatings` — this resolution is cheap and
 * wants to track a fresh track start about as quickly as the now-playing poller itself notices
 * one, not the slower 12s cadence the booth log's own page uses for its narrative history view. */
const ATTRIBUTION_POLL_INTERVAL_MS = 5000;

export interface NowPlayingTasteAttribution {
  /** The stamped booth-log row's own id — the taste-thumb POST target (SPEC F84.1, F84.6). */
  boothLogRowId: number;
  /** The persona stamped on that row at air time (SPEC F84.6) — resolved to a name via
   * `usePersonaDirectory` by the caller, never assumed to be "whichever persona is active now". */
  personaId: number;
}

/** Newest-first (SPEC F72.2) — the first track-started, persona-stamped row IS the latest one.
 * Defensively tolerant of a malformed/unexpected payload (a misrouted fetch mock in an unrelated
 * spec, a future wire change): anything that doesn't look like a real `BoothLogPage` resolves to
 * `null` rather than throwing mid-render. */
function latestTrackStartAttribution(page: BoothLogPage | null): NowPlayingTasteAttribution | null {
  if (page === null || !Array.isArray(page.entries)) return null;
  const row = page.entries.find((entry) => entry.kind === "track-started" && typeof entry.personaId === "number");
  if (row === undefined || typeof row.personaId !== "number") return null;
  return { boothLogRowId: row.id, personaId: row.personaId };
}

/**
 * Resolves the now-playing surface's taste-thumb target (SPEC F84.1, F84.6; STORY-215) —
 * `BoothLogController`'s own doc comment is explicit that there is no separate now-playing thumb
 * endpoint: "resolve to the latest track-start booth-log row, then call this same route." This
 * hook is that resolution, polled independently of the now-playing/play-history/ratings pollers
 * already on this page (F16.6). It deliberately does NOT try to match the resolved row against
 * whatever `fetchNowPlaying` currently reports — a persona-less airing, or a row that predates the
 * `personaId` column, resolves to `null`, so the caller renders no control at all (F84.6), the
 * same gating a stamped booth-log row applies to itself.
 */
export function useNowPlayingTasteAttribution(): NowPlayingTasteAttribution | null {
  const poll = usePoll(() => fetchBoothLogPage(), { intervalMs: ATTRIBUTION_POLL_INTERVAL_MS });
  return useMemo(() => latestTrackStartAttribution(poll.data), [poll.data]);
}
