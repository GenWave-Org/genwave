"use client";

import type { ReactNode } from "react";
import { EmptyState } from "@/components/ui/empty-state";
import { Skeleton } from "@/components/ui/skeleton";
import { formatClockTime, formatDurationCell } from "@/lib/format-clock";
import { isCatalogMediaId, isRateable, type PlayHistoryEntry, type RatingEntry } from "@/lib/broadcast-api";
import { RatingControls, type RatingControlsValue } from "../_components/RatingControls";
import { DEFAULT_RATING } from "./useLiveRatings";

interface PlayHistoryTableProps {
  entries: PlayHistoryEntry[] | null;
  error: boolean;
  /** Test-only injection point for `formatClockTime`; production omits this and gets the browser's local zone. */
  timeZone?: string;
  /** Current rating per catalog mediaId (SPEC F33.9, STORY-114) — composed by `LiveView` on the
   * poll cadence via `useLiveRatings`, keyed the same way as `entries[].mediaId`. */
  ratings: ReadonlyMap<string, RatingEntry>;
  /** Folds a vote/never-play response for a row back into the shared ratings state — the
   * response body IS the fresh state (F33.11: no refetch). */
  onRatingChange: (entry: RatingEntry) => void;
}

/**
 * The full play-history ring (SPEC F28.7–F28.8, F28.10, STORY-088), newest
 * first per the wire contract — time (HH:MM), title, artist, gain dB
 * right-aligned with tabular numerals, plain duration where present (blank
 * otherwise, SPEC F50.5–F50.6), plus a rating cell per catalog-id row (SPEC
 * F33.11, STORY-114): score chip + vote-up/vote-down/never-play controls.
 * `tts:*` entries render no rating cell contents at all — voting on a TTS
 * patter segment isn't meaningful (it isn't a catalog media row).
 * Unlike the dashboard's `RecentPlays` (which shows the latest 5), every
 * returned ring entry renders here — the ring defaults to 50 (F16.1) and
 * Live is the full-picture view. No source column: the wire
 * `PlayHistoryEntry` carries no `source` field (STORY-088 amended at Q5
 * review). A quiet unavailable hint replaces/augments the table on poll
 * failure per the shared usePoll degrade contract (SPEC F28.8 AC5).
 */
export function PlayHistoryTable({
  entries,
  error,
  timeZone,
  ratings,
  onRatingChange,
}: PlayHistoryTableProps): ReactNode {
  const loading = entries === null && !error;
  const neverLoaded = entries === null && error;

  return (
    <section aria-label="Play history">
      <p className="text-[0.7rem] font-semibold uppercase tracking-[0.14em] text-accent-2">
        Play history
      </p>

      {loading && (
        <div className="mt-3 space-y-2">
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
        </div>
      )}

      {neverLoaded && <p className="mt-3 text-[0.85rem] text-mute">Play history — unavailable</p>}

      {entries !== null && entries.length === 0 && (
        <div className="mt-3">
          <EmptyState
            title="Nothing in the play-history ring yet"
            reason="Entries appear here as tracks air."
          />
        </div>
      )}

      {entries !== null && entries.length > 0 && (
        // AC2 (SPEC F28.13): scrolls sideways inside this container at 390px —
        // the page body itself never does.
        <div className="mt-3 overflow-x-auto">
          <table className="w-full border-collapse text-[0.85rem]">
            <thead>
              <tr className="border-b-2 border-line text-left">
                <th
                  scope="col"
                  className="py-2 pr-3 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2"
                >
                  Time
                </th>
                <th
                  scope="col"
                  className="py-2 pr-3 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2"
                >
                  Title
                </th>
                <th
                  scope="col"
                  className="py-2 pr-3 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2"
                >
                  Artist
                </th>
                <th
                  scope="col"
                  className="py-2 pr-3 text-right text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2"
                >
                  Gain
                </th>
                <th
                  scope="col"
                  className="py-2 pr-3 text-right text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2"
                >
                  Duration
                </th>
                <th
                  scope="col"
                  className="py-2 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2"
                >
                  Rating
                </th>
              </tr>
            </thead>
            <tbody>
              {entries.map((entry, index) => (
                <tr key={`${entry.mediaId}-${index}`} className="border-b border-line last:border-b-0">
                  <td className="py-2 pr-3 tabular-nums text-mute">
                    {formatClockTime(entry.startedAt, { timeZone })}
                  </td>
                  <td className="py-2 pr-3 text-ink">{entry.title ?? "Unknown track"}</td>
                  <td className="py-2 pr-3 text-mute">{entry.artist ?? "Unknown artist"}</td>
                  <td className="py-2 pr-3 text-right tabular-nums text-ink">{entry.gainDb.toFixed(2)} dB</td>
                  <td className="py-2 pr-3 text-right tabular-nums text-ink">
                    {formatDurationCell(entry.durationMs)}
                  </td>
                  <td className="py-2">
                    {/* gh-#99: safe-scope rows (rateable: false) get no control, not a disabled one */}
                    {isCatalogMediaId(entry.mediaId) && isRateable(ratings.get(entry.mediaId)) && (
                      <RatingControls
                        mediaId={entry.mediaId}
                        value={ratings.get(entry.mediaId) ?? DEFAULT_RATING}
                        onChange={(next: RatingControlsValue) =>
                          onRatingChange({ mediaId: entry.mediaId, ...next })
                        }
                      />
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {error && entries !== null && (
        <p className="mt-2 text-[0.75rem] text-mute">Play history unavailable — retrying…</p>
      )}
    </section>
  );
}
