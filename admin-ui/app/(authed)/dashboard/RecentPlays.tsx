"use client";

import type { ReactNode } from "react";
import { EmptyState } from "@/components/ui/empty-state";
import { Skeleton } from "@/components/ui/skeleton";
import { formatClockTime, formatDurationCell } from "@/lib/format-clock";
import type { PlayHistoryEntry } from "@/lib/broadcast-api";

interface RecentPlaysProps {
  entries: PlayHistoryEntry[] | null;
  error: boolean;
  /** Test-only injection point for `formatClockTime`; production omits this and gets the browser's local zone. */
  timeZone?: string;
}

const RECENT_COUNT = 5;

/**
 * The most recent play-history entries (SPEC F28.7), newest first per the
 * wire contract — time (HH:MM), title, artist, gain dB right-aligned with
 * tabular numerals, plain duration where present (blank otherwise, SPEC
 * F50.5–F50.6). A quiet unavailable hint replaces/augments the table on poll
 * failure per the shared usePoll degrade contract (SPEC F28.8 AC5).
 */
export function RecentPlays({ entries, error, timeZone }: RecentPlaysProps): ReactNode {
  const loading = entries === null && !error;
  const neverLoaded = entries === null && error;

  return (
    <section aria-label="Recent plays">
      <p className="text-[0.7rem] font-semibold uppercase tracking-[0.14em] text-accent-2">
        Recent plays
      </p>

      {loading && (
        <div className="mt-3 space-y-2">
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
        </div>
      )}

      {neverLoaded && <p className="mt-3 text-[0.85rem] text-mute">Recent plays — unavailable</p>}

      {entries !== null && entries.length === 0 && (
        <div className="mt-3">
          <EmptyState
            title="Nothing has aired yet"
            reason="Recent plays appear here as tracks air."
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
                  className="py-2 text-right text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2"
                >
                  Duration
                </th>
              </tr>
            </thead>
            <tbody>
              {entries.slice(0, RECENT_COUNT).map((entry, index) => (
                <tr key={`${entry.mediaId}-${index}`} className="border-b border-line last:border-b-0">
                  <td className="py-2 pr-3 tabular-nums text-mute">
                    {formatClockTime(entry.startedAt, { timeZone })}
                  </td>
                  <td className="py-2 pr-3 text-ink">{entry.title ?? "Unknown track"}</td>
                  <td className="py-2 pr-3 text-mute">{entry.artist ?? "Unknown artist"}</td>
                  <td className="py-2 pr-3 text-right tabular-nums text-ink">{entry.gainDb.toFixed(2)} dB</td>
                  <td className="py-2 text-right tabular-nums text-ink">{formatDurationCell(entry.durationMs)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {error && entries !== null && (
        <p className="mt-2 text-[0.75rem] text-mute">Recent plays unavailable — retrying…</p>
      )}
    </section>
  );
}
