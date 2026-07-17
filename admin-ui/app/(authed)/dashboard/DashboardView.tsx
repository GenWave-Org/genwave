"use client";

import type { ReactNode } from "react";
import { usePoll } from "@/lib/use-poll";
import { fetchNowPlaying, fetchPlayHistory, fetchStatus } from "@/lib/broadcast-api";
import { NowPlayingCard } from "../_components/NowPlayingCard";
import { RecentPlays } from "./RecentPlays";
import { StatusTiles } from "./StatusTiles";

interface DashboardViewProps {
  /** Test-only injection point, threaded to the clock formatters; production omits this and gets the browser's local zone. */
  timeZone?: string;
}

/**
 * The dashboard's live content (SPEC F28.7–F28.8, STORY-087): now-playing
 * hero, station status tiles, and recent plays — each independently polled
 * through the shared `usePoll` hook (5 s cadence, Page Visibility
 * pause/resume, quiet per-card degrade on fetch failure, no toasts).
 * Purely client-driven — there is no SSR-prefetched initial state, so every
 * card starts in its loading/skeleton state until its first poll resolves.
 */
export function DashboardView({ timeZone }: DashboardViewProps = {}): ReactNode {
  const nowPlaying = usePoll(fetchNowPlaying);
  const status = usePoll(fetchStatus);
  const playHistory = usePoll(fetchPlayHistory);

  return (
    <div className="space-y-6">
      <NowPlayingCard state={nowPlaying.data} error={nowPlaying.error} />
      <StatusTiles status={status.data} error={status.error} timeZone={timeZone} />
      <RecentPlays entries={playHistory.data} error={playHistory.error} timeZone={timeZone} />
    </div>
  );
}
