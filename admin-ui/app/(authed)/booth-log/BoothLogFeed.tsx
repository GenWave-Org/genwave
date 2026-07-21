"use client";

import type { ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/empty-state";
import { Skeleton } from "@/components/ui/skeleton";
import { formatUpSince } from "@/lib/format-clock";
import type { BoothLogEntry } from "@/lib/booth-log-api";
import { cn } from "@/lib/utils";

interface BoothLogFeedProps {
  entries: BoothLogEntry[] | null;
  error: boolean;
  nextBefore: string | null;
  loadingMore: boolean;
  loadMoreError: boolean;
  onLoadMore: () => void;
  /** Test-only injection point for the timestamp formatter; production omits this and gets the browser's local zone. */
  timeZone?: string;
}

/** Human copy for the three narrative kinds this feed's writer produces (SPEC F72.1,
 * BoothLogWriter.cs) — the raw kind itself, still rendered as-is, covers anything future. */
const KIND_LABELS: Record<string, string> = {
  "track-started": "Track started",
  "patter-aired": "Patter aired",
  "mode-changed": "Mode changed",
};

/** 3px-radius bordered chip per design-aesthetic's source-tag convention (SourceChip,
 * NeverPlayBadge): patter-aired takes the `--accent` treatment the skill reserves for TTS/patter
 * content, mode-changed gets the quiet brass (`--accent-2`) "system note" treatment, and
 * track-started stays the plainest `--line`/`--mute` neutral. A kind this UI doesn't specifically
 * style still renders — raw text, the same neutral treatment — rather than disappearing. */
function BoothLogKindBadge({ kind }: { kind: string }): ReactNode {
  const styles: Record<string, string> = {
    "track-started": "border-line text-mute",
    "patter-aired": "border-accent text-accent",
    "mode-changed": "border-accent-2 text-accent-2",
  };
  return (
    <span
      className={cn(
        "inline-flex w-fit items-center rounded-[3px] border px-1.5 py-0.5 text-[0.68rem] font-semibold uppercase tracking-[0.08em]",
        styles[kind] ?? "border-line text-mute"
      )}
    >
      {KIND_LABELS[kind] ?? kind}
    </span>
  );
}

/**
 * The booth log's narrative feed (PLAN T40, STORY-195, SPEC F72.1-F72.2): newest-first rows of
 * occurred-at / kind badge / summary — "what did it say at 9:14" answerable from this table
 * alone. Loading/empty/error idioms match the Live page's `PlayHistoryTable` (skeleton rows,
 * `EmptyState`, a quiet unavailable hint on a poll failure that keeps whatever was already
 * loaded) — composed here through `useBoothLogFeed` instead of `usePoll` directly, since this
 * page additionally accumulates "Load more" pages (see that hook's doc comment for the
 * refresh/paging interaction this renders).
 */
export function BoothLogFeed({
  entries,
  error,
  nextBefore,
  loadingMore,
  loadMoreError,
  onLoadMore,
  timeZone,
}: BoothLogFeedProps): ReactNode {
  const loading = entries === null && !error;
  const neverLoaded = entries === null && error;

  return (
    <section aria-label="Booth log">
      <p className="text-[0.7rem] font-semibold uppercase tracking-[0.14em] text-accent-2">
        Booth log
      </p>

      {loading && (
        <div className="mt-3 space-y-2">
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
        </div>
      )}

      {neverLoaded && <p className="mt-3 text-[0.85rem] text-mute">Booth log — unavailable</p>}

      {entries !== null && entries.length === 0 && (
        <div className="mt-3">
          <EmptyState
            title="Nothing in the booth log yet"
            reason="Entries appear here as tracks start, patter airs, and the mode changes."
          />
        </div>
      )}

      {entries !== null && entries.length > 0 && (
        <div className="mt-3 overflow-x-auto">
          <table className="w-full border-collapse text-[0.85rem]">
            <thead>
              <tr className="border-b-2 border-line text-left">
                <th
                  scope="col"
                  className="py-2 pr-3 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2"
                >
                  Occurred
                </th>
                <th
                  scope="col"
                  className="py-2 pr-3 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2"
                >
                  Kind
                </th>
                <th
                  scope="col"
                  className="py-2 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2"
                >
                  Summary
                </th>
              </tr>
            </thead>
            <tbody>
              {entries.map((entry, index) => (
                <tr key={`${entry.occurredAt}-${index}`} className="border-b border-line last:border-b-0">
                  <td className="py-2 pr-3 whitespace-nowrap tabular-nums text-mute">
                    {formatUpSince(entry.occurredAt, { timeZone })}
                  </td>
                  <td className="py-2 pr-3">
                    <BoothLogKindBadge kind={entry.kind} />
                  </td>
                  <td className="py-2 text-ink">{entry.summary}</td>
                </tr>
              ))}
            </tbody>
          </table>

          {nextBefore !== null && (
            <div className="mt-3 flex flex-col items-start gap-2">
              <Button variant="secondary" onClick={onLoadMore} disabled={loadingMore}>
                {loadingMore ? "Loading…" : "Load more"}
              </Button>
              {loadMoreError && (
                <p className="text-[0.75rem] text-mute">Couldn&rsquo;t load more — try again.</p>
              )}
            </div>
          )}
        </div>
      )}

      {error && entries !== null && (
        <p className="mt-2 text-[0.75rem] text-mute">Booth log unavailable — retrying…</p>
      )}
    </section>
  );
}
